using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TorchesOut {
    // Per-torch state: the "Always on" checkbox (saved), a pending delayed switch-off (not saved),
    // and the periodic outdoor-torch check.
    public class CompTorchesOut : ThingComp {
        public bool alwaysOn;

        // Game tick at which to put this torch out, or -1. Transient: after a load the torch keeps
        // its saved on/off state and the next room exit reschedules it.
        public int pendingOffTick = -1;

        public CompFlickable Flick { get; private set; }

        public override void PostSpawnSetup(bool respawningAfterLoad) {
            base.PostSpawnSetup(respawningAfterLoad);
            Flick = parent.GetComp<CompFlickable>();
        }

        public override void PostExposeData() {
            base.PostExposeData();
            // Saved only when checked, so a save loaded without the mod just ignores the leftover value.
            Scribe_Values.Look(ref alwaysOn, "torchesOutAlwaysOn", false);
        }

        public void ScheduleOff(int delayTicks) {
            pendingOffTick = Find.TickManager.TicksGame + delayTicks;
        }

        public override void CompTick() {
            base.CompTick();
            int tick = Find.TickManager.TicksGame;
            if (pendingOffTick >= 0 && tick >= pendingOffTick) {
                pendingOffTick = -1;
                // Re-check at fire time: someone may have come back, or the room may have changed.
                RoomLights.TryDelayedOff(this);
            }
            // Outdoor rules on the same interval as the delay setting. Every torch checks on the
            // same tick, so any toggles land in one frame and the lighting/mesh rebuild runs once.
            int interval = System.Math.Max(1, TorchesOutMod.Settings.offDelayTicks);
            if (tick % interval == 0 && RoomLights.OutdoorRulesEnabled) {
                RoomLights.EvaluateEnvironment(this);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra() {
            foreach (Gizmo g in base.CompGetGizmosExtra()) {
                yield return g;
            }
            if (parent.Faction != Faction.OfPlayer) {
                yield break;
            }

            yield return new Command_Toggle {
                defaultLabel = "TorchesOut_AlwaysOn".Translate(),
                defaultDesc = "TorchesOut_AlwaysOnDesc".Translate(),
                icon = parent.def.uiIcon,
                isActive = () => alwaysOn,
                toggleAction = () => {
                    alwaysOn = !alwaysOn;
                    // Checking it relights a torch this mod had put out.
                    if (alwaysOn && Flick != null) {
                        pendingOffTick = -1;
                        RoomLights.TrySetTorch(Flick, true);
                    }
                }
            };
        }
    }
}
