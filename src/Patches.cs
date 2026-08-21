using DolocTown;
using HarmonyLib;

namespace DolocQuestTracker
{
    /// <summary>
    /// Tick pump. Patching the game's own Update means we only run while a player body
    /// exists -- no work on the title or loading screens. Same trick the minimap mod uses,
    /// and in this game it is the only pump that stays alive (our MonoBehaviour stops
    /// receiving Update after OnEnable), so Core is driven from here.
    /// </summary>
    [HarmonyPatch(typeof(BodyController), "Update")]
    internal static class BodyController_Update_Patch
    {
        private static void Postfix()
        {
            Core.Tick();
        }
    }

    /// <summary>
    /// Every game event funnels through DolocAPI.Broadcast, so one hook invalidates our
    /// snapshot on submits, seed unlocks and inventory changes. Polling still runs on top:
    /// LinearInventory exposes no change event, so chest/bot paths can slip past events.
    /// </summary>
    [HarmonyPatch(typeof(DolocAPI), "Broadcast", new System.Type[] { typeof(GameEventType), typeof(GameEventArgs) })]
    internal static class DolocAPI_Broadcast_Patch
    {
        private static void Postfix(GameEventType type)
        {
            Core.OnGameEvent(type);
        }
    }
}
