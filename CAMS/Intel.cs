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
            IgcTgt = "[FLT-TG]";


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
            //m.CtrlScreens.Add("missiles", new ListScreen(() => _missiles[_cat].Count, 4, sprites, s =>
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

        public void InterceptorParams(bool k, bool r, long kc) => Main.SendParams(k, false, false, false, true, r, kc);

    }
}