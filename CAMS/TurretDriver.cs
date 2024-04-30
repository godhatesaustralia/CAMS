using Sandbox.Game.AI.Navigation;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;
using System.Security.Cryptography.X509Certificates;
using Sandbox.ModAPI.Interfaces;

namespace IngameScript
{
    //public class TurretComp : CompBase
    //{
    //    public Dictionary<string, RotorTurret> TurretsRest = new Dictionary<string, RotorTurret>();
    //    List<string> Names = new List<string>();
    //    public IMyGridTerminalSystem GTS => Manager.Terminal;
    //    public TurretComp(string n) : base(n, UpdateFrequency.Update1)
    //    {
    //    }
    //    public override void Setup(CombatManager m)
    //    {
    //        Turrets.Clear();
    //        Manager = m;
    //        Manager.Terminal.GetBlocksOfType<IMyMotorStator>(null, (b) =>
    //        {
    //            if (b.CubeGrid.EntityId == m.Controller.CubeGrid.EntityId && b.CustomName.Contains("Azimuth") && !b.CustomName.Contains(Lib.array))
    //            {
    //                var t = new RotorTurret(b, this);
    //                t.Setup(ref m);
    //                if (t.NoCAMS)
    //                    TurretsRest.Add(t.Name, t);
    //                else if (t.Elevation != null)
    //                {
    //                    Turrets.Add(t.Name, t);
    //                    Names.Add(t.Name);
    //                }

    //            }
    //            return true;
    //        });
    //        Manager.Screens.Add(Name, new Screen(() => Turrets.Count, new MySprite[]
    //        { new MySprite(SpriteType.TEXT, "", new Vector2(20, 112), null, Lib.Green, Lib.vb, 0, 0.925f),// 1. TUR NAME
    //          new MySprite(SpriteType.TEXT, "AZ\nEL", new Vector2(20, 160), null, Lib.Green, Lib.vb, 0, 1.825f),// 2. ANGLE HDR
    //          new MySprite(SpriteType.TEXT, "", new Vector2(132, 164), null, Lib.Green, Lib.vb, 0, 0.9125f),// 3. ANGLE DATA
    //          new MySprite(SpriteType.TEXT, "", new Vector2(488, 160), null, Lib.Green, Lib.vb, (TextAlignment)1, 1.825f),// 4. RPM
    //          new MySprite(SpriteType.TEXT, "", new Vector2(20, 348), null, Lib.Green, Lib.vb, 0, 0.925f)// 5. WPNS
    //        }, (s) =>
    //        {
    //            var turret = Turrets[Names[s.ptr]];
    //            string n = turret.Name;
    //            var ct = 14 - turret.Name.Length;
    //            for (; ct-- > 0;)
    //                n += " ";
    //            s.SetData(n + $"{s.ptr + 1}/{Turrets.Count}", 0);
    //            s.SetData($"TGT {MathHelper.ToDegrees(turret.aziTgt).ToString("##0.#")}°\nCUR {turret.aziDeg().ToString("##0.#")}°\nTGT {MathHelper.ToDegrees(turret.elTgt).ToString("##0.#")}°\nCUR {turret.elDeg().ToString("##0.#")}°", 2);
    //            s.SetData($"{turret.Azimuth.TargetVelocityRPM}\n{turret.Elevation.TargetVelocityRPM}", 3);
    //            string cnd = "";
    //            foreach (var cond in turret.Conditions)
    //                cnd += $"{cond.Key} " + (cond.Value ? "T/" : "F/");
    //            s.SetData(cnd, 4);
    //            //s.SetData("WEAPONS- " + (turret.isShoot ? " ENABLED" : "INACTIVE"), 4);
    //        }));
    //    }

    //    public override void Update(UpdateFrequency u)
    //    {
    //        if (Manager.Targets.Count != 0)
    //        {
    //            foreach (var tur in Turrets.Values)
    //            {
    //                if (tur.ActiveCTC) continue;
    //                tur.SelectTarget(ref Manager.Targets, ref Manager.Gravity);
    //                // TEMP ONLY
    //                if (Handoff.Contains(tur.Name))
    //                    if (tur.tEID == -1)
    //                        continue;
    //                    else TakeControl(tur.Name, Lib.sn);

    //                tur.AimAndTrigger();
    //                tur.Update();
    //            }

    //        }
    //        else
    //        {
    //            foreach (var tr in TurretsRest.Values)
    //                if (!tr.ActiveCTC)
    //                    tr.Rest();

    //            foreach (var t in Turrets.Values)
    //                t.Rest();
    //        }
    //        foreach (var ti in Turrets)
    //            Debug += $"\n{ti.Key}";
    //    }
    //}

}

