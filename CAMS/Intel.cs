using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public class Intel : CompBase
    {
        IMyIntergridCommunicationSystem IGC => Main.IGC;
        IMyBroadcastListener
            _FLT, _TGT;
        IMyRadioAntenna _antenna, _backup;
        SortedList<string,SortedList<string, long>> _missiles = new SortedList<string, SortedList<string, long>>();  // format: group tag => missile name + pos => 
        Dictionary<long, double> _rtOffsets = new Dictionary<long, double>();
        bool _fixedRange, _useNetwork, _useBackup = false;
        string _mslTag, _cat;
        string[] _grpTags;
        long _key = -1;
        // deliberately omitting remote fire
        const string
            IgcFleet = "[FLT-CA]",
            IgcTgt = "[FLT-TG]", // :pranked:
            IgcParams = "IGC_MSL_PAR_MSG",
            IgcHoming = "IGC_MSL_HOM_MSG",
            IgcBeamRiding = "IGC_MSL_OPT_MSG",
            IgcIff = "IGC_IFF_PKT",
            IgcFire = "IGC_MSL_FIRE_MSG",
            Igcregister = "IGC_MSL_REG_MSG";

        public Intel(string n) : base(n, Lib.u10 | Lib.u100)
        {
            
        }

        public override void Setup(Program m)
        {
            Main = m;
            _FLT = IGC.RegisterBroadcastListener(IgcFleet);
            _TGT = IGC.RegisterBroadcastListener(IgcTgt);
            using (var p = new iniWrap())
                if (p.CustomData(Main.Me))
                {
                    var h = Lib.HDR;
                    _fixedRange = p.Bool(h, "fixedAntennaRange", true);
                    _useNetwork = p.Bool(h, "network", false);
                    _mslTag = p.String(h, "grpTag", "WHAM");
                    _grpTags = p.String(h, "mslTypes", "MSL\nEKV").Split('\n');
                    
                }
            Main.Terminal.GetBlocksOfType<IMyRadioAntenna>(null, a =>
            {
                if (a.CustomName.Contains("Main"))
                    _antenna = a;
                else if (a.CustomName.Contains("Backup"))
                    _backup = a;
                return true;
            });
            var l = new SortedList<string, long>();
            foreach (var t in _grpTags)
            {
                l.Clear();
                Main.Terminal.GetBlocksOfType<IMyProgrammableBlock>(null, b =>
                {
                    if (b.CustomName.Contains(t))
                    {
                        var n = b.CustomName.Split();
                        for (int i = 0; i < n.Length; i++)
                            if (n[i].Contains(t))
                                l.Add(n[i], b.EntityId);
                    }
                    return true;
                });
                if (l.Count > 0)
                    _missiles.Add(t, l);
            }
            var sprites = new MySprite[]
           {
                new MySprite(Lib.TXT, "", new Vector2(24, 112), null, Lib.GRN, Lib.VB, 0, 1.75f),// 1. TUR NAME
                new MySprite(Lib.TXT, "", new Vector2(24, 200), null, Lib.GRN, Lib.VB, 0, 0.8195f),
           };
            Commands.Add("group", b =>
            {
                if (_missiles.ContainsKey(b.Argument(2)))
                    _cat = b.Argument(2);
            });
            //m.Screens.Add("missiles", new ListScreen(() => _missiles[_cat].Count, 4, sprites, s =>
            //{
                
            //}));
        }

        public override void Update(UpdateFrequency u)
        {
            if (_antenna.Closed)
            {
                _useBackup = true;
                _key = _backup.EntityId;
            }
            // nah
            //if (_useNetwork)
            //{
            //    MyIGCMessage msg = new MyIGCMessage();
            //    if (F % 11 == 0)
            //        while (_FLT.HasPendingMessage)
            //        {
            //            msg = _FLT.AcceptMessage();
            //            if (msg.Data is double)
            //                _rtOffsets.Add(msg.Source, (double)msg.Data);
            //        }
            //    else if (F % 4 == 0)
            //        while (_TGT.HasPendingMessage) 
            //        {

            //            msg = _TGT.AcceptMessage();
            //            if (msg.Data is MyTuple<MyTuple<long, long, long, int, bool>, MyTuple<Vector3D, Vector3D, MatrixD, double>>)
            //            {
            //                var dat = (MyTuple<MyTuple<long, long, long, int, bool>, MyTuple<Vector3D, Vector3D, MatrixD, double>>)msg.Data;
            //                if (Targets.isNew(dat.Item1.Item1))

            //            }

            //        }
            //}

        }

        void FillMatrix(ref Matrix3x3 mat, ref Vector3D col0, ref Vector3D col1, ref Vector3D col2)
        {
            mat.M11 = (float)col0.X;
            mat.M21 = (float)col0.Y;
            mat.M31 = (float)col0.Z;

            mat.M12 = (float)col1.X;
            mat.M22 = (float)col1.Y;
            mat.M32 = (float)col1.Z;

            mat.M13 = (float)col2.X;
            mat.M23 = (float)col2.Y;
            mat.M33 = (float)col2.Z;
        }

        void SendWhamTarget(Vector3D hitPos, Vector3D tPos, Vector3D tVel, Vector3D preciseOffset, Vector3D myPos, double elapsed, long tEID, long keycode)
        {
            Matrix3x3 mat1 = new Matrix3x3();
            FillMatrix(ref mat1, ref hitPos, ref tPos, ref tVel);

            Matrix3x3 mat2 = new Matrix3x3();
            FillMatrix(ref mat2, ref preciseOffset, ref myPos, ref Vector3D.Zero);

            var msg = new MyTuple<Matrix3x3, Matrix3x3, float, long, long>
            {
                Item1 = mat1,
                Item2 = mat2,
                Item3 = (float)elapsed,
                Item4 = tEID,
                Item5 = keycode,
            };

            IGC.SendBroadcastMessage(IgcHoming, msg);
        }
        public void InterceptorParams(bool k, bool r, long kc) => SendParams(k, false, false, false, true, r, kc);

        byte BoolToByte(bool value) => value ? (byte)1 : (byte)0;

        void SendParams(bool kill, bool stealth, bool spiral, bool topdown, bool precise, bool retask, long keycode)
        {
            byte packed = 0;
            packed |= BoolToByte(kill);
            packed |= (byte)(BoolToByte(stealth) << 1);
            packed |= (byte)(BoolToByte(spiral) << 2);
            packed |= (byte)(BoolToByte(topdown) << 3);
            packed |= (byte)(BoolToByte(precise) << 4);
            packed |= (byte)(BoolToByte(retask) << 5);

            var msg = new MyTuple<byte, long>
            {
                Item1 = packed,
                Item2 = keycode
            };

            IGC.SendBroadcastMessage(IgcParams, msg);
        }


    }
}