using HarmonyLib;
using UnityEngine;
using Verse;

namespace TorchesOut {
    public class TorchesOutSettings : ModSettings {
        public bool relightOnEnter = true;
        public bool onlyWhenEmpty = true;
        public bool offInDaytime = true;
        public bool outdoorOnlyNearPeople = true;
        public bool debugLogging = false;
        public int offDelayTicks = DefaultOffDelayTicks;

        public const int DefaultOffDelayTicks = 30;
        public const int MaxOffDelayTicks = 300;

        public override void ExposeData() {
            Scribe_Values.Look(ref relightOnEnter, "relightOnEnter", true);
            Scribe_Values.Look(ref onlyWhenEmpty, "onlyWhenEmpty", true);
            Scribe_Values.Look(ref offInDaytime, "offInDaytime", true);
            Scribe_Values.Look(ref outdoorOnlyNearPeople, "outdoorOnlyNearPeople", true);
            Scribe_Values.Look(ref offDelayTicks, "offDelayTicks", DefaultOffDelayTicks);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
        }
    }

    public class TorchesOutMod : Mod {
        public static TorchesOutSettings Settings;

        public TorchesOutMod(ModContentPack content) : base(content) {
            Settings = GetSettings<TorchesOutSettings>();
            new Harmony("hyperchomp.torchesout").PatchAll();
        }

        public override string SettingsCategory() => "TorchesOut";

        public override void DoSettingsWindowContents(Rect inRect) {
            var list = new Listing_Standard();
            list.Begin(inRect);

            // Indoor rooms: driven by people entering and leaving.
            list.CheckboxLabeled("TorchesOut_RelightOnEnter".Translate(), ref Settings.relightOnEnter);
            list.CheckboxLabeled("TorchesOut_OnlyWhenEmpty".Translate(), ref Settings.onlyWhenEmpty, "TorchesOut_OnlyWhenEmptyDesc".Translate());
            list.GapLine();

            // Outdoor torches: checked periodically.
            list.CheckboxLabeled("TorchesOut_OffInDaytime".Translate(), ref Settings.offInDaytime, "TorchesOut_OffInDaytimeDesc".Translate());
            list.CheckboxLabeled("TorchesOut_OutdoorOnlyNearPeople".Translate(), ref Settings.outdoorOnlyNearPeople, "TorchesOut_OutdoorOnlyNearPeopleDesc".Translate());
            list.GapLine();

            // Game ticks: 60 per second at normal speed, so the interval shrinks in real time at higher speeds.
            string delayLabel = "TorchesOut_OffDelay".Translate(Settings.offDelayTicks, (Settings.offDelayTicks / 60f).ToString("0.##"));
            Settings.offDelayTicks = (int)list.SliderLabeled(delayLabel, Settings.offDelayTicks, 0f, TorchesOutSettings.MaxOffDelayTicks,
                tooltip: "TorchesOut_OffDelayDesc".Translate());
            list.CheckboxLabeled("TorchesOut_DebugLogging".Translate(), ref Settings.debugLogging);
            list.End();
        }

        public static void Debug(string msg) {
            if (Settings.debugLogging) {
                Log.Message("[TorchesOut] " + msg);
            }
        }
    }
}
