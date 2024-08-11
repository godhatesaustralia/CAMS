using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
	public class GyroCtrl
	{
		IMyGyro _gyro;
		byte _yaw, _pitch, _roll;
		static Action<IMyGyro, float>[] _profiles =
		{
			(g, v) => { g.Yaw = -v; },
			(g, v) => { g.Yaw = v; },
			(g, v) => { g.Pitch = -v; },
			(g, v) => { g.Pitch = v; },
			(g, v) => { g.Roll = -v; },
			(g, v) => { g.Roll = v; }
		};
		public void Init(IMyGyro g, IMyShipController c)
		{
			_gyro = g;
			_yaw = SetRelDir(_gyro.WorldMatrix.GetClosestDirection(c.WorldMatrix.Up));
			_pitch = SetRelDir(_gyro.WorldMatrix.GetClosestDirection(c.WorldMatrix.Left));
			_roll = SetRelDir(_gyro.WorldMatrix.GetClosestDirection(c.WorldMatrix.Forward));
		}

		static byte SetRelDir(Base6Directions.Direction dir)
		{
			switch (dir)
			{
				case Base6Directions.Direction.Up:
					return 1;
				default:
				case Base6Directions.Direction.Down:
					return 0;
				case Base6Directions.Direction.Left:
					return 2;
				case Base6Directions.Direction.Right:
					return 3;
				case Base6Directions.Direction.Forward:
					return 4;
				case Base6Directions.Direction.Backward:
					return 5;
			}
		}

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
				_profiles[_yaw](_gyro, y);
				_profiles[_pitch](_gyro, p);
				_profiles[_roll](_gyro, r);
			}
		}
	}

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

	public class Missile
	{
		public long TEID, LastF;
		const int K_Z = 3;
		Program _p;
		IMyRemoteControl _ctrl;
		IMyShipConnector _ctor;
		IMyShipMergeBlock _merge;
		IMyBatteryBlock _batt;
		IMyGasTank _tank;

		IMyGyro _gyro;
		byte _gYaw, _gPitch, _gRoll;
		int _cachePtr = 0;
		Vector3I[] _blockPosCache;
		IMyTerminalBlock[] _partsCache;

		//List<IMyCameraBlock> _sensors = new List<IMyCameraBlock>();
		List<IMyThrust> _thrust = new List<IMyThrust>();
		List<IMyWarhead> _warhead = new List<IMyWarhead>();
		PDCtrl _yaw, _pitch;

		public Missile(Program p, IMyTerminalBlock b)
		{
			_p = p;
		}

		public void ReloadMissile(IMyCubeBlock b)
		{
			if (CollectMissileBlocks(b))
			{
				for (; _cachePtr++ < _partsCache.Length;)
				{
					var t = _partsCache[_cachePtr];
					if (t == null) continue;
					// note - this setup makes it necessary to have controller as very item in both cache arrays
					// setup program MUST take this into account
					if (t is IMyRemoteControl) _ctrl = (IMyRemoteControl)t;
					else if (t is IMyGyro) _gyro = (IMyGyro)t;
					else if (t is IMyBatteryBlock) { _batt = (IMyBatteryBlock)t; _batt.ChargeMode = ChargeMode.Recharge; }
					else if (t is IMyThrust) _thrust.Add((IMyThrust)t);
					else if (t is IMyWarhead) _warhead.Add((IMyWarhead)t);
					else if (t is IMyShipConnector) { _ctor = (IMyShipConnector)t; _ctor.Connect(); }
					else if (t is IMyShipMergeBlock) _merge = (IMyShipMergeBlock)t;
					else if (t is IMyGasTank) { _tank = (IMyGasTank)t; _tank.Stockpile = true; }
				}
				_cachePtr = 0;
			}
		}

		bool CollectMissileBlocks(IMyCubeBlock b)
		{
			while (_cachePtr < _blockPosCache.Length)
			{
				var slim = b.CubeGrid.GetCubeBlock(_blockPosCache[_cachePtr]);
				if (slim != null && slim.IsFullIntegrity)
				{
					if (_cachePtr < _partsCache.Length && slim.FatBlock is IMyTerminalBlock)
						_partsCache[_cachePtr] = (IMyTerminalBlock)slim.FatBlock;
					_cachePtr++;
				}
				else return false;
			}
			_cachePtr = 0;
			return true;
		}

		void Clear()
		{
			_ctrl = null;
			_warhead.Clear();
			_thrust.Clear();
		}

		public bool Init(IMyTerminalBlock o)
		{
			using (var q = new iniWrap())
				if (!q.CustomData(o))
					return false;
				else
				{
					int 
						pg = q.Int(Lib.H, "pGain", 10),
						dg = q.Int(Lib.H, "dGain", 5);

					_yaw = new PDCtrl(pg, dg, 10);
					_pitch = new PDCtrl(pg, dg, 10);

					var p = q.String(Lib.H, "cache");
					if (string.IsNullOrEmpty(p))
						return false;

					var l = new List<Vector3I>();
					foreach (string line in o.CustomData.Split('\n'))
					{
						if (string.IsNullOrEmpty(line)) continue;
						line.Trim('|');

						var v = line.Split('.');
						int x, y, z;

						if (!int.TryParse(v[0], out x) || !int.TryParse(v[1], out y) || !int.TryParse(v[2], out z))
							return false;

						Vector3I[] vec =
						{
							-Base6Directions.GetIntVector(o.Orientation.Left),
							Base6Directions.GetIntVector(o.Orientation.Up),
							-Base6Directions.GetIntVector(o.Orientation.Forward)
						};

						l.Add((x * vec[0]) + (y * vec[1]) + (z * vec[2]) + o.Position);
					}

					_blockPosCache = new Vector3I[l.Count];
					_partsCache = new IMyTerminalBlock[l.Count];

					for (int i = 0; i < l.Count; i++)
						_blockPosCache[i] = l[i];
					// temporary. need to find a better way
					// of switching behavior to do proper setup
					// -- need to set gyro profiles only once.
					var ok = CollectMissileBlocks(o);
					if (ok)
					{
						ReloadMissile(o);
						if (_ctrl != null && _gyro == null)
						{
							_gYaw = Lib.SetGyroRelDir(_gyro.WorldMatrix.GetClosestDirection(_ctrl.WorldMatrix.Up));
							_gPitch =Lib.SetGyroRelDir(_gyro.WorldMatrix.GetClosestDirection(_ctrl.WorldMatrix.Left));
							_gRoll = Lib.SetGyroRelDir(_gyro.WorldMatrix.GetClosestDirection(_ctrl.WorldMatrix.Forward));

						}
					}
					return ok;
				}
			}
		}
	}