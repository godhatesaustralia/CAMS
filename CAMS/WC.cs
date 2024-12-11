using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript
{
    class FakeTemplate
    {
        static long ID;
        TargetProvider Targets;
        IMyProgrammableBlock Me;
        /// copy paste following code in or inline it for wc \\\
        public WCAPI CAPI;
        Dictionary<MyDetectedEntityInfo, float> _threats = new Dictionary<MyDetectedEntityInfo, float>();
        void PerformScan()
        {
            _threats.Clear();
            CAPI.GetSortedThreats(Me, _threats);
            int rel, t;
            MyDetectedEntityInfo i;
            if (_threats.Count > 0)
            {
                Targets.Clear();
                foreach (var info in _threats.Keys)
                {
                    if (info.IsEmpty())
                        continue;
                    i = info;
                    rel = (int)info.Relationship;
                    t = (int)info.Type;
                    if (rel == 1 || rel == 5 || (t != 2 && t != 3)) // owner or friends
                        continue;
                    Targets.AddOrUpdate(ref i, ID, ID);
                }
            }
        }

    }
    public class WCAPI
    {
        private Action<IMyTerminalBlock, bool, bool, int> _toggleWeaponFire;
        private Func<IMyTerminalBlock, int, MyDetectedEntityInfo> _getWeaponTarget;
        private Func<IMyTerminalBlock, bool> _hasCoreWeapon;
        private Action<IMyTerminalBlock, IDictionary<MyDetectedEntityInfo, float>> _getSortedThreats;
        private Action<IMyTerminalBlock, ICollection<MyDetectedEntityInfo>> _getObstructions;
        private Func<long, int, MyDetectedEntityInfo> _getAiFocus;
        private Action<IMyTerminalBlock, long, int> _setWeaponTarget;
        private Func<long, bool> _hasGridAi;
        private Func<IMyTerminalBlock, int, bool, bool, bool> _isWeaponReadyToFire;
        private Func<IMyTerminalBlock, int, MyTuple<Vector3D, Vector3D>> _getWeaponScope;
        private Func<IMyTerminalBlock, float> _getHeatLevel;
        private Func<IMyTerminalBlock, long, int, bool> _setAiFocus;
        private Func<IMyTerminalBlock, ICollection<string>, int, bool> _getTurretTargetTypes;

        static public void Activate(IMyTerminalBlock pbBlock, ref WCAPI apiHandle)
        {
            if (apiHandle != null)
                return;

            var dict = pbBlock.GetProperty("WcPbAPI")?.As<IReadOnlyDictionary<string, Delegate>>().GetValue(pbBlock);
            if (dict == null)
                return;

            apiHandle = new WCAPI();
            apiHandle.ApiAssign(dict);
        }

        public void ApiAssign(IReadOnlyDictionary<string, Delegate> delegates)
        {
            AssignMethod(delegates, "GetWeaponTarget", ref _getWeaponTarget);
            AssignMethod(delegates, "ToggleWeaponFire", ref _toggleWeaponFire);
            AssignMethod(delegates, "HasCoreWeapon", ref _hasCoreWeapon);
            AssignMethod(delegates, "GetSortedThreats", ref _getSortedThreats);
            AssignMethod(delegates, "GetObstructions", ref _getObstructions);
            AssignMethod(delegates, "GetAiFocus", ref _getAiFocus);
            AssignMethod(delegates, "SetAiFocus", ref _setAiFocus);
            AssignMethod(delegates, "SetWeaponTarget", ref _setWeaponTarget);
            AssignMethod(delegates, "HasGridAi", ref _hasGridAi);
            AssignMethod(delegates, "IsWeaponReadyToFire", ref _isWeaponReadyToFire);
            AssignMethod(delegates, "GetWeaponScope", ref _getWeaponScope);
            AssignMethod(delegates, "GetHeatLevel", ref _getHeatLevel);
            AssignMethod(delegates, "GetTurretTargetTypes", ref _getTurretTargetTypes);
        }

        private void AssignMethod<T>(IReadOnlyDictionary<string, Delegate> delegates, string name, ref T field) where T : class
        {
            if (delegates == null)
            {
                field = null;
                return;
            }
            Delegate del;
            if (!delegates.TryGetValue(name, out del))
                throw new Exception($"{GetType().Name} wc1: {name} {typeof(T)}");
            field = del as T;
            if (field == null)
                throw new Exception(
                    $"{GetType().Name} wc2: {name} {typeof(T)} {del.GetType()}");
        }
        public void Shoot(IMyTerminalBlock weapon, bool on, bool allWeapons, int weaponId = 0) =>
            _toggleWeaponFire?.Invoke(weapon, on, allWeapons, weaponId);
        public MyDetectedEntityInfo? GetWeaponTarget(IMyTerminalBlock weapon, int weaponId = 0) =>
            _getWeaponTarget?.Invoke(weapon, weaponId);

        public bool HasCoreWeapon(IMyTerminalBlock weapon) => _hasCoreWeapon?.Invoke(weapon) ?? false;

        public void GetSortedThreats(IMyTerminalBlock pBlock, IDictionary<MyDetectedEntityInfo, float> collection) =>
            _getSortedThreats?.Invoke(pBlock, collection);

        public void GetObstructions(IMyTerminalBlock pBlock, ICollection<MyDetectedEntityInfo> collection) =>
            _getObstructions?.Invoke(pBlock, collection);

        public MyDetectedEntityInfo? GetAiFocus(long shooter, int priority = 0) => _getAiFocus?.Invoke(shooter, priority);

        public bool SetAiFocus(IMyTerminalBlock pBlock, long target, int priority = 0) =>
            _setAiFocus?.Invoke(pBlock, target, priority) ?? false;

        public void SetWeaponTarget(IMyTerminalBlock weapon, long target, int weaponId = 0) =>
            _setWeaponTarget?.Invoke(weapon, target, weaponId);

        public bool HasGridAi(long entity) => _hasGridAi?.Invoke(entity) ?? false;

        public bool IsWeaponReadyToFire(IMyTerminalBlock weapon, int weaponId = 0, bool anyWeaponReady = true,
            bool shootReady = false) =>
            _isWeaponReadyToFire?.Invoke(weapon, weaponId, anyWeaponReady, shootReady) ?? false;

        public MyTuple<Vector3D, Vector3D> GetWeaponScope(IMyTerminalBlock weapon, int weaponId) =>
            _getWeaponScope?.Invoke(weapon, weaponId) ?? new MyTuple<Vector3D, Vector3D>();

        public float GetHeatLevel(IMyTerminalBlock weapon) => _getHeatLevel?.Invoke(weapon) ?? 0f;

        public bool GetTurretTargetTypes(IMyTerminalBlock weapon, IList<string> collection, int weaponId = 0) =>
            _getTurretTargetTypes?.Invoke(weapon, collection, weaponId) ?? false;
    }
}
