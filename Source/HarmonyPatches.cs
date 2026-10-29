using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace TorchesOut {
    // TryEnterNextPathCell is where a walking pawn's Position changes. It has several early
    // returns (doors, blockers), so remember the start cell and compare afterwards.
    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    public static class Patch_PathFollower_TryEnterNextPathCell {
        public static void Prefix(Pawn ___pawn, out IntVec3 __state) {
            __state = ___pawn.Position;
        }

        public static void Postfix(Pawn ___pawn, IntVec3 __state) {
            // This runs inside every colonist's movement, so never let an exception escape:
            // it would repeat every tick and could stall the pawn. Log it once instead.
            try {
                if (___pawn.Position != __state && ___pawn.Spawned && RoomLights.TriggersLights(___pawn)) {
                    RoomLights.Notify_PawnMoved(___pawn, __state);
                }
            } catch (System.Exception e) {
                Log.ErrorOnce("[TorchesOut] Error while updating room torches; further errors suppressed: " + e, 0x70C4E5);
            }
        }
    }

    // Vanilla drains fuel from an unroofed refuelable in rain even when it's switched off. For a
    // switched-off torch the rain drain is the only work CompTick has left (normal burn already
    // checks the switch, and the base CompTick is empty), so skipping it means "off" costs no fuel.
    [HarmonyPatch(typeof(CompRefuelable), nameof(CompRefuelable.CompTick))]
    public static class Patch_CompRefuelable_CompTick {
        public static bool Prefix(CompRefuelable __instance, CompFlickable ___flickComp) {
            if (___flickComp == null || ___flickComp.SwitchIsOn) {
                return true;
            }
            ThingDef def = __instance.parent.def;
            return def != TorchDefOf.TorchLamp && def != TorchDefOf.TorchWallLamp;
        }
    }

    // CompFireOverlay only checks fuel, so it would keep drawing flames on a switched-off torch.
    [HarmonyPatch(typeof(CompFireOverlay), nameof(CompFireOverlay.PostDraw))]
    public static class Patch_CompFireOverlay_PostDraw {
        public static bool Prefix(CompFireOverlay __instance) {
            return __instance.parent.GetComp<CompFlickable>()?.SwitchIsOn ?? true;
        }
    }
}
