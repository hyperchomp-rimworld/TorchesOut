using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TorchesOut {
    [DefOf]
    public static class TorchDefOf {
        public static ThingDef TorchLamp;
        public static ThingDef TorchWallLamp;

        static TorchDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(TorchDefOf));
    }

    public static class RoomLights {
        // The player's intended switch state (set by the vanilla toggle gizmo). Read-only use:
        // we only relight torches the player wants on, so manually-extinguished torches stay off.
        static readonly AccessTools.FieldRef<CompFlickable, bool> wantSwitchOn =
            AccessTools.FieldRefAccess<CompFlickable, bool>("wantSwitchOn");

        static readonly List<CompFlickable> torchBuffer = new List<CompFlickable>();

        // Pawns whose movement makes us check rooms.
        public static bool TriggersLights(Pawn p) => p.IsColonist || p.IsPrisonerOfColony;

        // Indoors = a real enclosed room. Anything else (null, doorway, map-edge/outdoor, unroofed) is skipped.
        public static bool IsIndoors(Room room) {
            return room != null
                && !room.Dereferenced
                && !room.IsDoorway
                && room.ProperRoom              // false for rooms touching the map edge, i.e. the outdoors
                && !room.PsychologicallyOutdoors
                && !room.UsesOutdoorTemperature;
        }

        // Called after a colonist or prisoner actually changed cells.
        public static void Notify_PawnMoved(Pawn pawn, IntVec3 from) {
            Map map = pawn.Map;
            Room oldRoom = from.GetRoom(map);
            Room newRoom = pawn.Position.GetRoom(map);
            if (oldRoom == newRoom) {
                return;
            }

            if (IsIndoors(oldRoom) && ShouldGoDark(oldRoom)) {
                int delay = TorchesOutMod.Settings.offDelayTicks;
                if (delay <= 0) {
                    SetRoomTorches(oldRoom, false);
                } else {
                    ScheduleRoomOff(oldRoom, delay);
                }
            }

            if (IsIndoors(newRoom)) {
                Notify_Entered(newRoom);
            }
        }

        static bool ShouldGoDark(Room room) => !TorchesOutMod.Settings.onlyWhenEmpty || !AnyoneIn(room);

        // Debounce: the room's torches go out offDelayTicks after it empties, unless someone
        // comes back first. Rescheduling pushes the deadline back.
        static void ScheduleRoomOff(Room room, int delay) {
            CollectTorches(room);
            foreach (CompFlickable flick in torchBuffer) {
                if (flick.SwitchIsOn && flick.parent.GetComp<CompTorchesOut>() is CompTorchesOut comp && !comp.alwaysOn) {
                    comp.ScheduleOff(delay);
                }
            }
            torchBuffer.Clear();
            TorchesOutMod.Debug($"room {room.ID} empty, torches out in {delay} ticks");
        }

        // Someone entered: cancel pending switch-offs, and relight if enabled.
        static void Notify_Entered(Room room) {
            CollectTorches(room);
            bool relight = TorchesOutMod.Settings.relightOnEnter;
            foreach (CompFlickable flick in torchBuffer) {
                if (flick.parent.GetComp<CompTorchesOut>() is CompTorchesOut comp) {
                    comp.pendingOffTick = -1;
                }
                if (relight && TrySetTorch(flick, true)) {
                    TorchesOutMod.Debug($"{flick.parent} at {flick.parent.Position} switched on (room {room.ID})");
                }
            }
            torchBuffer.Clear();
        }

        // Called from CompTorchesOut.CompTick when a scheduled switch-off comes due.
        public static void TryDelayedOff(CompTorchesOut comp) {
            Thing torch = comp.parent;
            if (!torch.Spawned || !(torch.TryGetComp<CompFlickable>() is CompFlickable flick)) {
                return;
            }
            Room room = torch.GetRoom();
            if (IsIndoors(room) && ShouldGoDark(room) && TrySetTorch(flick, false)) {
                TorchesOutMod.Debug($"{torch} at {torch.Position} switched off after delay (room {room.ID})");
            }
        }

        // Who keeps a room lit: colonists (even asleep), awake humanlike player pawns, prisoners,
        // and anyone lying in a medical bed (e.g. guests or downed visitors being treated).
        static bool KeepsLightsOn(Pawn p) {
            if (!p.RaceProps.Humanlike) {
                return false;
            }
            if (p.IsColonist || p.IsPrisonerOfColony) {
                return true;
            }
            if (p.Faction == Faction.OfPlayer && p.Awake()) {
                return true;
            }
            return p.CurrentBed() is Building_Bed bed && bed.Medical;
        }

        // Room-local: only the pawns listed in this room's regions.
        static bool AnyoneIn(Room room) {
            List<Region> regions = room.Regions;
            for (int r = 0; r < regions.Count; r++) {
                List<Thing> pawns = regions[r].ListerThings.ThingsInGroup(ThingRequestGroup.Pawn);
                for (int i = 0; i < pawns.Count; i++) {
                    if (pawns[i] is Pawn p && KeepsLightsOn(p) && p.GetRoom() == room) {
                        return true;
                    }
                }
            }
            return false;
        }

        static void SetRoomTorches(Room room, bool on) {
            CollectTorches(room);
            foreach (CompFlickable flick in torchBuffer) {
                if (TrySetTorch(flick, on)) {
                    TorchesOutMod.Debug($"{flick.parent} at {flick.parent.Position} switched {(on ? "on" : "off")} (room {room.ID})");
                }
            }
            torchBuffer.Clear();
        }

        // ---- Outdoor torches: daylight and nearby people ----

        // Per-map state, computed at most once per map per tick: whether it's daytime, and where
        // the people who keep lights on are (outdoor torches only burn near them).
        static int envTick = -1;
        static Map envMap;
        static bool envDaytime;
        static readonly List<IntVec3> peopleCells = new List<IntVec3>();
        static readonly List<Room> peopleRooms = new List<Room>();

        static void RefreshEnvironment(Map map) {
            int tick = Find.TickManager.TicksGame;
            if (tick == envTick && map == envMap) {
                return;
            }
            envTick = tick;
            envMap = map;
            // Vanilla's definition of daytime: sun glow above 0.6, from sun position only.
            envDaytime = GenCelestial.IsDaytime(GenCelestial.CurCelestialSunGlow(map));
            peopleCells.Clear();
            peopleRooms.Clear();
            if (TorchesOutMod.Settings.outdoorOnlyNearPeople) {
                List<Pawn> pawns = map.mapPawns.AllHumanlikeSpawned;
                for (int i = 0; i < pawns.Count; i++) {
                    if (KeepsLightsOn(pawns[i])) {
                        peopleCells.Add(pawns[i].Position);
                        peopleRooms.Add(pawns[i].GetRoom());
                    }
                }
            }
        }

        public static bool OutdoorRulesEnabled => TorchesOutMod.Settings.offInDaytime || TorchesOutMod.Settings.outdoorOnlyNearPeople;

        // Someone who keeps lights on is in the torch's outdoor area (same room, so not behind
        // a wall) and within its light radius.
        static bool AnyoneNear(Thing torch, Room room) {
            float radius = torch.TryGetComp<CompGlower>()?.GlowRadius ?? 0f;
            float radiusSq = radius * radius;
            for (int i = 0; i < peopleCells.Count; i++) {
                if (peopleRooms[i] == room && (peopleCells[i] - torch.Position).LengthHorizontalSquared <= radiusSq) {
                    return true;
                }
            }
            return false;
        }

        // Outdoor torches are dark by day, and otherwise only lit while someone is near. Weather
        // needs no rule: an unlit torch loses no fuel to rain (Patch_CompRefuelable_CompTick).
        // Indoor torches are never suppressed: the enter/leave logic handles them, so an
        // occupied room is never left dark.
        public static bool Suppressed(Thing torch) {
            TorchesOutSettings s = TorchesOutMod.Settings;
            if (!torch.Spawned || !OutdoorRulesEnabled) {
                return false;
            }
            Room room = torch.GetRoom();
            if (room == null || IsIndoors(room)) {
                return false;
            }
            RefreshEnvironment(torch.Map);
            if (s.offInDaytime && envDaytime) {
                return true;
            }
            return s.outdoorOnlyNearPeople && !AnyoneNear(torch, room);
        }

        // Periodic check from CompTorchesOut.CompTick for outdoor torches: puts out suppressed
        // ones, and relights them when night falls or someone comes near.
        public static void EvaluateEnvironment(CompTorchesOut comp) {
            Thing torch = comp.parent;
            CompFlickable flick = comp.Flick;
            if (flick == null || torch.Faction != Faction.OfPlayer) {
                return;
            }
            if (Suppressed(torch)) {
                if (TrySetTorch(flick, false)) {
                    TorchesOutMod.Debug($"{torch} at {torch.Position} switched off (daytime, or nobody near)");
                }
                return;
            }
            if (flick.SwitchIsOn) {
                return;
            }
            Room room = torch.GetRoom();
            if (room == null || IsIndoors(room)) {
                return;
            }
            if (TrySetTorch(flick, true)) {
                TorchesOutMod.Debug($"{torch} at {torch.Position} relit (night, someone near)");
            }
        }

        // Switch directly (no flick job) and clear any pending flick designation. Refuses to:
        // - turn off an "Always on" torch.
        // - light a torch the player switched off by hand.
        // - light an outdoor torch during the day, or with nobody near it, unless it's "Always on".
        public static bool TrySetTorch(CompFlickable flick, bool on) {
            if (flick.SwitchIsOn == on || (on && !wantSwitchOn(flick))) {
                return false;
            }
            bool alwaysOn = flick.parent.GetComp<CompTorchesOut>() is CompTorchesOut comp && comp.alwaysOn;
            if (!on && alwaysOn) {
                return false;
            }
            if (on && !alwaysOn && Suppressed(flick.parent)) {
                return false;
            }
            flick.SwitchIsOn = on; // broadcasts FlickedOn/Off: glower, heat and fuel react
            if (flick.parent.Spawned) {
                flick.parent.Map.designationManager.TryRemoveDesignationOn(flick.parent, DesignationDefOf.Flick);
            }
            return true;
        }

        // Look only in the room's own regions. Both torch types stand on a floor cell of the room:
        // a wall torch occupies the cell in front of the wall it faces (see Placeworker_AttachedToWall),
        // so its own Position gives its room. The GetRoom check drops things a region lists but
        // that belong to a neighbouring room, and the Contains check drops duplicates.
        static void CollectTorches(Room room) {
            torchBuffer.Clear();
            List<Region> regions = room.Regions;
            for (int r = 0; r < regions.Count; r++) {
                AddTorches(regions[r].ListerThings.ThingsOfDef(TorchDefOf.TorchLamp), room);
                AddTorches(regions[r].ListerThings.ThingsOfDef(TorchDefOf.TorchWallLamp), room);
            }
        }

        static void AddTorches(List<Thing> things, Room room) {
            for (int i = 0; i < things.Count; i++) {
                Thing t = things[i];
                if (t.Faction != Faction.OfPlayer || t.GetRoom() != room) {
                    continue;
                }
                CompFlickable flick = t.TryGetComp<CompFlickable>();
                if (flick != null && !torchBuffer.Contains(flick)) {
                    torchBuffer.Add(flick);
                }
            }
        }
    }
}
