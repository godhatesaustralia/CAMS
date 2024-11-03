using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.AI;
using VRageMath;

namespace IngameScript
{
	// this is all alysius i have no fucking idea how it works
	public class FastSolver
	{
		public const double
			epsilon = 1E-6,
			cos120d = -0.5,
			sin120d = 0.86602540378,
			root3 = 1.73205080757,
			inv3 = 1.0 / 3.0,
			inv9 = 1.0 / 9.0,
			inv54 = 1.0 / 54.0;

		static double[] _t = new double[4];

		//Shortcut Ignoring Of Complex Values And Return Smallest Real Number
		public static double Solve(double a, double b, double c, double d, double e)
		{
			_t[0] = _t[1] = _t[2] = _t[3] = 0;
			if (Math.Abs(a) < epsilon) a = a >= 0 ? epsilon : -epsilon;
			double inva = 1 / a;

			b *= inva;
			c *= inva;
			d *= inva;
			e *= inva;

			double
				a3 = -c,
				b3 = b * d - 4 * e,
				c3 = -b * b * e - d * d + 4 * c * e;

			bool useMax = SolveCubic(a3, b3, c3, ref _t);
			double y = _t[0];
			if (useMax)
			{
				if (Math.Abs(_t[1]) > Math.Abs(y)) y = _t[1];
				if (Math.Abs(_t[2]) > Math.Abs(y)) y = _t[2];
			}

			double q1, q2, p1, p2, squ, u = y * y - 4 * e;
			if (Math.Abs(u) < epsilon)
			{
				q1 = q2 = y * 0.5;
				u = b * b - 4 * (c - y);

				if (Math.Abs(u) < epsilon)
				{
					p1 = p2 = b * 0.5;
				}
				else
				{
					squ = Math.Sqrt(u);
					p1 = (b + squ) * 0.5;
					p2 = (b - squ) * 0.5;
				}
			}
			else
			{
				squ = Math.Sqrt(u);
				q1 = (y + squ) * 0.5;
				q2 = (y - squ) * 0.5;

				double dm = 1 / (q1 - q2);
				p1 = (b * q1 - d) * dm;
				p2 = (d - b * q2) * dm;
			}

			double v1, v2;

			u = p1 * p1 - 4 * q1;
			if (u < 0)
			{
				v1 = double.MaxValue;
			}
			else
			{
				squ = Math.Sqrt(u);
				v1 = MinPosNZ(-p1 + squ, -p1 - squ) * 0.5;
			}

			u = p2 * p2 - 4 * q2;
			if (u < 0)
			{
				v2 = double.MaxValue;
			}
			else
			{
				squ = Math.Sqrt(u);
				v2 = MinPosNZ(-p2 + squ, -p2 - squ) * 0.5;
			}

			return MinPosNZ(v1, v2);
		}

		static bool SolveCubic(double a, double b, double c, ref double[] res)
		{
			double
				a2 = a * a,
				q = (a2 - 3 * b) * inv9,
				r = (a * (2 * a2 - 9 * b) + 27 * c) * inv54,
				r2 = r * r,
				q3 = q * q * q;

			if (r2 < q3)
			{
				double
					sqq = Math.Sqrt(q),
					t = r / (sqq * sqq * sqq);
				if (t < -1) t = -1;
				else if (t > 1) t = 1;

				t = Math.Acos(t);

				a *= inv3;
				q = -2 * sqq;

				double
					costv3 = Math.Cos(t * inv3),
					sintv3 = Math.Sin(t * inv3);

				res[0] = q * costv3 - a;
				res[1] = q * ((costv3 * cos120d) - (sintv3 * sin120d)) - a;
				res[2] = q * ((costv3 * cos120d) + (sintv3 * sin120d)) - a;

				return true;
			}
			else
			{
				double g = -Math.Pow(Math.Abs(r) + Math.Sqrt(r2 - q3), inv3);
				if (r < 0) g = -g;

				double h = g == 0 ? 0 : q / g;

				a *= inv3;

				res[0] = g + h - a;
				res[1] = -0.5 * (g + h) - a;
				res[2] = 0.5 * root3 * (g - h);

				if (Math.Abs(res[2]) < epsilon)
				{
					res[2] = res[1];
					return true;
				}
				return false;
			}
		}

		static double MinPosNZ(double a, double b)
		{
			if (a <= 0) return b > 0 ? b : double.MaxValue;
			else if (b <= 0) return a;
			else return Math.Min(a, b);
		}
	}

	public class Hardpoint : IComparable<Hardpoint>
	{
		public readonly string Name;
		public readonly float Reload;
		public IMyShipMergeBlock Base;
		byte _gYaw, _gPitch, _gRoll;
		bool _complete = false;
		double _aVal;
		int _cachePtr = 0, _gainP, _gainD, _evnMax, _evnMin;
		Vector3I[] _blockPosCache;
		IMyTerminalBlock[] _partsCache;

		public Hardpoint(string n, float r)
		{
			Reload = r;
			Name = n;
		}

		public bool Init(IMyShipMergeBlock h, ref Missile m)
		{
			using (var q = new iniWrap())
				if (!q.CustomData(h))
					return false;
				else
				{
					Base = h;
					_aVal = q.Double(Lib.H, "accel", -1);
					_gainP = q.Int(Lib.H, "pGain", 10);
					_gainD = q.Int(Lib.H, "dGain", 5);
					_evnMax = q.Int(Lib.H, "evnMaxDist", 0);
					_evnMin = q.Int(Lib.H, "evnMinDist", 0);

					if (!q.GyroYPR(Lib.H, "ypr", out _gYaw, out _gPitch, out _gRoll))
						return false;

					var p = q.String(Lib.H, "cache");
					if (string.IsNullOrEmpty(p))
						return false;

					var l = new List<Vector3I>();
					foreach (string line in p.Split('\n'))
					{
						if (string.IsNullOrEmpty(line)) continue;
						line.Trim('|');

						var v = line.Split('.');
						int x, y, z;

						if (!int.TryParse(v[0], out x) || !int.TryParse(v[1], out y) || !int.TryParse(v[2], out z))
							return false;

						Vector3I[] vec =
						{
							-Base6Directions.GetIntVector(h.Orientation.Left),
							Base6Directions.GetIntVector(h.Orientation.Up),
							-Base6Directions.GetIntVector(h.Orientation.Forward)
						};

						l.Add((x * vec[0]) + (y * vec[1]) + (z * vec[2]) + h.Position);
					}

					_blockPosCache = new Vector3I[l.Count];
					_partsCache = new IMyTerminalBlock[l.Count];

					for (int i = 0; i < l.Count; i++)
						_blockPosCache[i] = l[i];

					m = new Missile(_aVal, _gYaw, _gPitch, _gRoll, _gainP, _gainD, _evnMax, _evnMin);
					return _blockPosCache != null;
				}
		}

		public bool CollectMissileBlocks()
		{
			if (!_complete)
			{
				while (_cachePtr < _blockPosCache.Length)
				{
					var slim = Base.CubeGrid.GetCubeBlock(_blockPosCache[_cachePtr]);
					if (slim != null && slim.IsFullIntegrity)
					{
						if (_cachePtr < _partsCache.Length && slim.FatBlock is IMyTerminalBlock)
							_partsCache[_cachePtr] = (IMyTerminalBlock)slim.FatBlock;
						_cachePtr++;
					}
					else return false;
				}
				_cachePtr = 1;
				_complete = true;
			}
			return true;
		}

		public bool IsMissileReady(ref Missile m)
		{
			if (!_complete) return false;

			m.Clear();
			if (!m.TrySetup(ref _cachePtr, ref _partsCache))
				return false;

			return true;
		}

		public int CompareTo(Hardpoint o)
		{
			if (Reload == o.Reload)
				return Base.IsConnected ? 1 : Name.CompareTo(o.Name);
			else return Reload < o.Reload ? -1 : 1;
		}
	}

	// |======TODO LIST=======|
	// -camera raycast/prox fuse
	// -cruise fuel conservation
	// -offset launch vector
	public class Missile
	{
		const int DEF_UPDATE = 8, DEF_STAT = 29, EVN_ADJ = 600;
		const double TOL = 0.00001, CAM_ANG = 0.707, PD_AIM_LIM = 6.3;
		public long MEID, TEID, NextUpdateF, NextStatusF, NextEvnAdjF;
		public string IDTG;
		public IMyRemoteControl Controller;
		IMyShipConnector _ctor;
		IMyShipMergeBlock _merge;
		IMyGyro _gyro;
		IMyBatteryBlock _batt;
		List<IMyCameraBlock> _sensors = new List<IMyCameraBlock>();
		List<IMyGasTank> _tanks = new List<IMyGasTank>();
		List<IMyThrust> _thrust = new List<IMyThrust>();
		List<IMyWarhead> _warhead = new List<IMyWarhead>();

		bool _evade, _checkAccel;
		byte _gYaw, _gPitch, _gRoll;
		double _accel, _evMax, _evMin;

		Program _p;
		PDCtrl _yawCtrl, _pitchCtrl;
		MatrixD _viewMat;
		Vector3D _pos, _cmd, _evn;

		public Missile(double a, byte y, byte p, byte r, int pg, int dg, int eMx, int eMn)
		{
			_accel = a;
			_gYaw = y;
			_gPitch = p;
			_gRoll = r;
			_evade = eMx != 0 && eMn != 0;
			_evMax = eMx;
			_evMin = eMn;
			_yawCtrl = new PDCtrl(pg, dg, DEF_UPDATE);
			_pitchCtrl = new PDCtrl(pg, dg, DEF_UPDATE);
		}

		#region gyro
		static Action<IMyGyro, float>[] _profiles =
		{
			(g, v) => { g.Yaw = -v; },
			(g, v) => { g.Yaw = v; },
			(g, v) => { g.Pitch = -v; },
			(g, v) => { g.Pitch = v; },
			(g, v) => { g.Roll = -v; },
			(g, v) => { g.Roll = v; }
		};

		public void SetGyroOverride(bool move, float y, float p, float r)
		{
			// check
			if (!_gyro.IsFunctional)
			{
				_gyro.Enabled = _gyro.GyroOverride = false;
				_gyro.Yaw = _gyro.Pitch = _gyro.Roll = 0f;
			}
			else if (move)
			{
				_gyro.Enabled = move;
				_profiles[_gYaw](_gyro, y);
				_profiles[_gPitch](_gyro, p);
				_profiles[_gRoll](_gyro, r);
			}
		}
		#endregion

		public bool TrySetup(ref int p, ref IMyTerminalBlock[] c)
		{
			if (c[0] is IMyRemoteControl)
				Controller = (IMyRemoteControl)c[0];
			else return false;
			for (; p++ < c.Length;)
			{
				var t = c[p];
				if (t == null) continue;
				// note - this setup makes it necessary to have controller as first item in both cache arrays
				// setup program MUST take this into account
				else if (t is IMyGyro) _gyro = (IMyGyro)t;
				else if (t is IMyBatteryBlock) { _batt = (IMyBatteryBlock)t; _batt.ChargeMode = ChargeMode.Recharge; }
				else if (t is IMyThrust) _thrust.Add((IMyThrust)t);
				else if (t is IMyWarhead) _warhead.Add((IMyWarhead)t);
				else if (t is IMyShipConnector) { _ctor = (IMyShipConnector)t; _ctor.Connect(); }
				else if (t is IMyShipMergeBlock) _merge = (IMyShipMergeBlock)t;
				else if (t is IMyGasTank) { _tanks.Add((IMyGasTank)t); _tanks[_tanks.Count - 1].Stockpile = true; }
				else if (t is IMyCameraBlock) { _sensors.Add((IMyCameraBlock)t); _sensors[_sensors.Count - 1].EnableRaycast = true; }
			}
			p = 0;
			MEID = Controller.EntityId;
			IDTG = MEID.ToString("X").Remove(0, 11);
			return true;
		}

		public void Clear()
		{
			Controller = null;
			_gyro = null;
			_merge = null;
			_ctor = null;
			_batt = null;

			_tanks.Clear();
			_thrust.Clear();
			_warhead.Clear();
			_sensors.Clear();

			_yawCtrl.Reset();
			_pitchCtrl.Reset();

			MEID = TEID = -1;
			IDTG = "NULL";
		}

		public void Launch(long teid, Program p)
		{
			TEID = teid;
			_p = p;
			NextUpdateF = p.F + DEF_UPDATE;
			NextStatusF = NextUpdateF + DEF_STAT;
			_merge.Enabled = false;
			foreach (var g in _tanks)
				g.Stockpile = false;
			_batt.ChargeMode = ChargeMode.Discharge;
			_ctor.Disconnect();
			_checkAccel = _accel == -1;
			foreach (var t in _thrust)
			{
				t.Enabled = true;
				t.ThrustOverridePercentage = 1;
			}
			
		}

		public void Kill()
		{
			foreach (var tk in _tanks)
				tk.Enabled = false;
			foreach(var th in _thrust)
				th.Enabled = false;
			foreach (var w in _warhead)
			{
				w.IsArmed = true;
				w.Detonate();
			}
			_gyro.Enabled = _batt.Enabled = false;
			Clear();
		}

		public bool Inoperable()
		{
			NextStatusF += DEF_STAT;
			if (!_batt.IsFunctional || !_gyro.IsFunctional || !Controller.IsFunctional)
				return true;
			
			if (_batt.Closed || _gyro.Closed || Controller.Closed)
				return true;
			
			_checkAccel |= _merge.Closed || _ctor.Closed;

			var fuel = 0d;
			int i = 0;

			for (; i < _tanks.Count; i++)
			{
				_checkAccel |= _tanks[i].Closed;
				if (!_tanks[i].IsFunctional || _tanks[i].Closed)
					_tanks.RemoveAtFast(i);
				else fuel += _tanks[i].FilledRatio;
			}

			for (i = 0; i < _thrust.Count; i++)
			{
				_checkAccel |= _thrust[i].Closed;
				if (!_thrust[i].IsFunctional || _thrust[i].Closed)
					_thrust.RemoveAtFast(i);
			}

			return fuel <= TOL && _tanks.Count == 0 && _thrust.Count == 0;
		}

		public void Update(Target tgt)
		{
			if (tgt == null)
				return;

			#region nav
			_viewMat = MatrixD.Transpose(Controller.WorldMatrix);
			_pos = Controller.WorldMatrix.Translation;

			Vector3D
				rP = tgt.Position - _pos,
				rV = tgt.Velocity - Controller.GetShipVelocities().LinearVelocity,
				rA = tgt.Accel - Controller.GetNaturalGravity();

			// idk what im doing
			if (rP.LengthSquared() < tgt.Radius * tgt.Radius)
			{
				var w = _warhead[0];
				w.IsArmed = true;
				w.Detonate();
				_warhead.RemoveAtFast(0);
				if (_warhead.Count == 0)
					NextUpdateF += DEF_STAT;
				else return;
			}
			else NextUpdateF += DEF_UPDATE;

			if (_checkAccel)
			{
				_accel = 0;
				foreach (var th in _thrust)
					_accel += th.MaxEffectiveThrust;
				_accel /= Controller.CalculateShipMass().TotalMass;
			}

			double
				a = 0.25 * rA.LengthSquared() + _accel * _accel,
				b = rA.Dot(rV),
				c = rA.Dot(rP) + rV.LengthSquared(),
				d = 2 * rP.Dot(rV),
				e = rV.LengthSquared(),
				r = rP.Length(),
				t = FastSolver.Solve(a, b, c, d, e);

			if (t == double.MaxValue || double.IsNaN(t)) t = 1000;
			var icpt = tgt.Position + (rV * t) + (0.5 * rA * t * t);

			if (_evade)
			{
				if (_p.F >= NextEvnAdjF)
				{
					Lib.RandomNormalVector(ref _p.RNG, ref _cmd, ref _evn);
					_evn *= tgt.Radius;
					NextEvnAdjF += EVN_ADJ;
				}
				if (r < _evMax && r > _evMin)
					icpt += _evn;
			}

			_cmd = Vector3D.TransformNormal(icpt - _pos, ref _viewMat);
			#endregion

			#region aim
			double 
				aX = Math.Abs(_cmd.X), // abs x
				aY = Math.Abs(_cmd.Y), // abs y
				aZ = Math.Abs(_cmd.Z), // abs z
				y = Lib.HALF_PI, // yaw input
				p = Lib.HALF_PI; // pitch input

			if (aZ > TOL)
			{
				bool yFlip = aX > aZ, pFlip = aY > aZ;

				y = Lib.FastAT(Math.Max(yFlip ? (aZ / aX) : (aX / aZ), TOL));
				p = Lib.FastAT(Math.Max(pFlip ? (aZ / aY) : (aY / aZ), TOL));

				if (yFlip) y = Lib.HALF_PI - y;
				if (pFlip) p = Lib.HALF_PI - p;

				if (_cmd.Z > 0)
				{
					y = Lib.PI - y;
					p = Lib.PI - p;
				}
			}

			if (double.IsNaN(y)) y = 0;
			if (double.IsNaN(p)) p = 0;

			y *= Math.Sign(_cmd.X);
			p *= Math.Sign(_cmd.Y);

			y = _yawCtrl.Filter(y, 2);
			p = _pitchCtrl.Filter(p, 2);

			if (Math.Abs(y) + Math.Abs(p) > PD_AIM_LIM)
			{
				var adjust = PD_AIM_LIM / (Math.Abs(y) + Math.Abs(p));
				y *= adjust;
				p *= adjust;
			}

			SetGyroOverride(true, (float)y, (float)p, 0);
			#endregion

		}
	}
}