using BepInEx;
using BepInEx.Logging;
using R2API.Networking;
using R2API.Utils;

namespace ShareItemsWithMoreFun
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.bepis.r2api")]
    [BepInDependency(NetworkingAPI.PluginGUID)]
    [BepInDependency(RiskOfOptionsCompat.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.shareitemswithmorefun";
        public const string PluginName = "ShareItemsWithMoreFun";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModConfig.Init(Config);
            OptionPickupPrefab.Load();

            NetworkingAPI.RegisterMessageType<ChestOpenedMessage>();
            NetworkingAPI.RegisterMessageType<OwnerMessage>();

            ChestHooks.Install();
            OrbHooks.Install();
            ClientHooks.Install();
            LateJoinSync.Install();

            if (RiskOfOptionsCompat.Enabled)
            {
                RiskOfOptionsCompat.Register();
            }
        }

        private void Start()
        {
            // La liste des plugins n'est complète qu'une fois tous les Awake passés.
            Conflicts.WarnAboutKnownConflicts();
        }

        private void Update()
        {
            NetworkLookups.Tick();
        }

        internal static void Debug(string message)
        {
            if (ModConfig.VerboseLogging.Value)
            {
                Log.LogInfo(message);
            }
        }
    }
}
