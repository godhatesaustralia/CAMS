﻿using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
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
		int _gainP, _gainD;

		bool _complete = false;
		int _cachePtr = 0;
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
					_gainP = q.Int(Lib.H, "pGain", 10);
					_gainD = q.Int(Lib.H, "dGain", 5);
					
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

					m = new Missile(_gYaw, _gPitch, _gRoll, _gainP, _gainD);
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

	public class Missile
	{
		const int DEF_UPDATE = 10;
		public long MEID, TEID, LastF;
		public string IDTG;
		public IMyRemoteControl Controller;
		public IMyShipMergeBlock Hardpoint;
		IMyShipConnector _ctor;
		IMyShipMergeBlock _merge;
		IMyGyro _gyro;
		IMyBatteryBlock _batt;
		List<IMyCameraBlock> _sensors = new List<IMyCameraBlock>();
		List<IMyGasTank> _tanks = new List<IMyGasTank>();
		List<IMyThrust> _thrust = new List<IMyThrust>();
		List<IMyWarhead> _warhead = new List<IMyWarhead>();

		bool _checkAccel;
		byte _gYaw, _gPitch, _gRoll;
		double _accel;

		PDCtrl _yaw, _pitch;
		MatrixD _viewMat;
		Vector3D _pos, _cmd;

		public Missile(byte y, byte p, byte r, int pg, int dg)
		{
			_gYaw = y;
			_gPitch = p;
			_gRoll = r;
			_yaw = new PDCtrl(pg, dg, DEF_UPDATE);
			_pitch = new PDCtrl(pg, dg, DEF_UPDATE);
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

			_yaw.Reset();
			_pitch.Reset();

			MEID = TEID = -1;
			IDTG = "NULL";
		}

		public void Launch(long teid)
		{
			TEID = teid;
			_merge.Enabled = Hardpoint.Enabled = false;
			foreach (var g in _tanks)
				g.Stockpile = false;
			_batt.ChargeMode = ChargeMode.Discharge;
			_ctor.Disconnect();
			foreach (var t in _thrust)
			{
				t.Enabled = true;
				t.ThrustOverridePercentage = 1;
			}
		}

		public void Update(Target tgt)
		{
			if (tgt == null)
				return;

			#region nav
			_viewMat = MatrixD.Transpose(Controller.WorldMatrix);
			_pos = Controller.WorldMatrix.Translation;

			Vector3D
				grav = Controller.GetNaturalGravity(),
				rP = tgt.Position - _pos,
				rV = tgt.Velocity - Controller.GetShipVelocities().LinearVelocity,
				rA = tgt.Accel - Controller.GetNaturalGravity();

			double
				a = 0.25 * rA.LengthSquared() + _accel * _accel,
				b = rA.Dot(rV),
				c = rA.Dot(rP) + rV.LengthSquared(),
				d = 2 * rP.Dot(rV),
				e = rV.LengthSquared(),
				t = FastSolver.Solve(a, b, c, d, e);

			if (t == double.MaxValue || double.IsNaN(t)) t = 1000;
			var icpt = tgt.Position + (rV * t) + (0.5 * rA * t * t);
			_cmd = Vector3D.TransformNormal(icpt - _pos, ref _viewMat);
			#endregion

		}
	}
}