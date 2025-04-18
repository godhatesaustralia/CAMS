using System;
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
		public bool Complete = false;
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

					m = new Missile();
					return _blockPosCache != null;
				}
		}

		public bool CollectMissileBlocks()
		{
			if (!Complete)
			{
				while (_cachePtr < _blockPosCache.Length)
				{
					var slim = Base.CubeGrid.GetCubeBlock(_blockPosCache[_cachePtr]);
					if (slim != null && slim.IsFullIntegrity)
					{
						if (slim.FatBlock is IMyTerminalBlock)
							_partsCache[_cachePtr] = (IMyTerminalBlock)slim.FatBlock;
						_cachePtr++;

					}
					else return false;
				}
				_cachePtr = 1;
				Complete = true;
			}
			return true;
		}

		public bool IsMissileReady(ref Missile m)
		{
			bool r = Complete && m.TrySetup(ref _cachePtr, ref _partsCache);
			if (r)
			{
				Complete = false;
				m.Reset(_aVal, _gYaw, _gPitch, _gRoll, _gainP, _gainD, _evnMax, _evnMin);
				for (int i = 0; i < _partsCache.Length; i++)
					_partsCache[i] = null;
			}
			return r;
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
		const int DEF_UPDATE = 8, DEF_STAT = 29, ADJ_MIN = 500, ADJ_MAX = 1000, OFS_D = 400, FUSE_D = 220;
		const double TOL = 1E-5, CAM_ANG = .707, PROX_R = .265, PD_AIM_LIM = 6.3, MAX_TTK = 1E4, KILL_D = 12.75;
		public long MEID = -1, TEID, LastActiveF, NextUpdateF, NextStatusF, NextEvnAdjF;
		public bool Inoperable => _dead || !Controller.IsFunctional || Controller.Closed;
		public string IDTG = "N/A", DEBUG;
		public IMyRemoteControl Controller;
		IMyShipConnector _ctor;
		IMyShipMergeBlock _merge;
		IMyGyro _gyro;
		IMyBatteryBlock _batt;
		List<IMyCameraBlock> _sensors = new List<IMyCameraBlock>();
		List<IMyGasTank> _tanks = new List<IMyGasTank>();
		List<IMyThrust> _thrust = new List<IMyThrust>();
		List<IMyWarhead> _warhead = new List<IMyWarhead>();

		bool _evade, _selOfsPt, _checkAccel, _dead = true, _arm, _cams, _kill;
		byte _gYaw, _gPitch, _gRoll;
		double _range, _rSq, _accel, _evMaxSq, _evMinSq;

		Program _p;
		PDCtrl _yawCtrl = new PDCtrl(), _pitchCtrl = new PDCtrl();
		MatrixD _viewMat;
		Offset _ofs;
		Vector3D _pos, _cmd, _evn;

		#region msl-gyro
		static Action<IMyGyro, float>[] _profiles =
		{
			(g, v) => { g.Yaw = -v; },
			(g, v) => { g.Yaw = v; },
			(g, v) => { g.Pitch = -v; },
			(g, v) => { g.Pitch = v; },
			(g, v) => { g.Roll = -v; },
			(g, v) => { g.Roll = v; }
		};

		void AimGyro()
		{
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

			y = _yawCtrl.Filter(y * Math.Sign(_cmd.X), 2);
			p = _pitchCtrl.Filter(p * Math.Sign(_cmd.Y), 2);

			if (Math.Abs(y) + Math.Abs(p) > PD_AIM_LIM)
			{
				var adjust = PD_AIM_LIM / (Math.Abs(y) + Math.Abs(p));
				y *= adjust;
				p *= adjust;
			}

			_profiles[_gYaw](_gyro, (float)y);
			_profiles[_gPitch](_gyro, (float)p);
			_profiles[_gRoll](_gyro, 0);

			NextUpdateF += DEF_UPDATE;
		}

		#endregion

		public string Data()
		{
			if (MEID == -1) return "\n▮▮▮\n▮▮▮\n▮▮▮";

			double p = _batt.CurrentStoredPower / _batt.MaxStoredPower;
			var r = $"\n{p * 100:000}";

			p = 0;
			foreach (var t in _tanks)
				p += t.FilledRatio;
			p /= _tanks.Count;

			r += $"\n{p * 100:000}\n{(_ctor.IsConnected ? "LCK" : "OFF")}";
			return r;
		}

		#region msl-reset
		public void Reset(double a, byte y, byte p, byte r, int pg, int dg, int eMx, int eMn)
		{
			_accel = a;
			_gYaw = y;
			_gPitch = p;
			_gRoll = r;
			_evade = eMx != 0 && eMn != 0;
			_evMaxSq = eMx * eMx;
			_evMinSq = eMn * eMn;
			_dead = false;

			_yawCtrl.Reset(pg, dg, DEF_UPDATE);
			_pitchCtrl.Reset(pg, dg, DEF_UPDATE);
		}

		public bool TrySetup(ref int p, ref IMyTerminalBlock[] c)
		{
			if (c[0] is IMyRemoteControl)
				Controller = (IMyRemoteControl)c[0];
			else return false;
			for (; p < c.Length; p++)
			{
				var t = c[p];
				if (t == null) return false;
				// note - this setup makes it necessary to have controller as first item in both cache arrays
				// setup program MUST take this into account
				else if (t is IMyGyro) _gyro = (IMyGyro)t;
				else if (t is IMyBatteryBlock) { _batt = (IMyBatteryBlock)t; _batt.ChargeMode = ChargeMode.Recharge; }
				else if (t is IMyThrust) _thrust.Add((IMyThrust)t);
				else if (t is IMyWarhead) _warhead.Add((IMyWarhead)t);
				else if (t is IMyShipConnector) { _ctor = (IMyShipConnector)t; _ctor.Connect(); }
				else if (t is IMyShipMergeBlock) _merge = (IMyShipMergeBlock)t;
				else if (t is IMyGasTank) { var k = (IMyGasTank)t; k.Stockpile = true; _tanks.Add(k); }
				else if (t is IMyCameraBlock) { var s = (IMyCameraBlock)t; s.EnableRaycast = true; _sensors.Add(s); }
			}

			p = 0;
			MEID = Controller.EntityId;
			IDTG = MEID.ToString("X").Remove(0, 12);
			return true;
		}

		public void Clear()
		{
			_dead = true;

			foreach (var tk in _tanks)
				tk.Enabled = false;
			foreach (var th in _thrust)
				th.Enabled = false;
			foreach (var w in _warhead)
			{
				w.IsArmed = true;
				w.Detonate();
			}
			_gyro.Enabled = _batt.Enabled = false;

			Controller = null;
			_gyro = null;
			_merge = null;
			_ctor = null;
			_batt = null;

			_tanks.Clear();
			_thrust.Clear();
			_warhead.Clear();
			_sensors.Clear();

			MEID = TEID = -1;
			IDTG = "N/A";
		}
		#endregion

		#region msl-control
		public void CheckStatus()
		{
			NextStatusF += DEF_STAT;
			_dead |= !_batt.IsFunctional || !_gyro.IsFunctional || _batt.Closed || _gyro.Closed;

			if (_dead) return;

			_checkAccel |= _merge.Closed || _ctor.Closed;

			var fuel = 0d;
			int i = _tanks.Count;

			for (; --i >= 0;)
			{
				var t = _tanks[i];
				_checkAccel |= t.Closed;
				if (!t.IsFunctional || t.Closed)
					_tanks.RemoveAtFast(i);
				else fuel += t.FilledRatio;
			}

			for (i = _thrust.Count; --i >= 0;)
			{
				_checkAccel |= _thrust[i].Closed;
				if (!_thrust[i].IsFunctional || _thrust[i].Closed)
					_thrust.RemoveAtFast(i);
			}

			_dead |= fuel <= TOL || _tanks.Count == 0 || _thrust.Count == 0;
		}

		public void Launch(long teid, Program p)
		{
			TEID = teid;
			_p = p;

			LastActiveF = _p.F;
			NextUpdateF = p.F + DEF_UPDATE;
			NextStatusF = NextUpdateF + DEF_STAT;

			foreach (var g in _tanks)
				g.Stockpile = false;
			foreach (var t in _thrust)
			{
				t.Enabled = true;
				t.ThrustOverridePercentage = 1;
			}

			_ctor.Disconnect();
			_batt.ChargeMode = ChargeMode.Discharge;
			_merge.Enabled = _ctor.Enabled = _arm = false;

			_cams = _sensors.Count > 0;
			_checkAccel = _accel == -1;
			_gyro.GyroOverride = _selOfsPt = true;
		}

		public void Hold()
		{
			var v = Controller.GetShipVelocities().LinearVelocity;
			if (v.Length() < 1)
			{
				foreach (var t in _thrust)
					t.ThrustOverridePercentage = 0;
				return;
			}

			_selOfsPt = true;
			_viewMat = MatrixD.Transpose(Controller.WorldMatrix);
			_cmd = Vector3D.TransformNormal(-v, _viewMat);

			AimGyro();
		}
		#endregion

		public void Update(Target tgt)
		{
			if (tgt == null || _dead) return;

			if (!tgt.Engaged) _p.Targets.MarkEngaged(tgt.EID);

			LastActiveF = _p.F;
			_viewMat = MatrixD.Transpose(Controller.WorldMatrix);
			_pos = Controller.WorldMatrix.Translation;
			var vel = Controller.GetShipVelocities().LinearVelocity;

			var icpt = tgt.Center;
			if (_ofs.Frame != 0 && _range < OFS_D)
				icpt += Vector3D.TransformNormal(_ofs.Hit, tgt.Matrix);

			Vector3D
				rP = icpt - _pos,
				rV = tgt.Velocity - vel,
				rA = tgt.Accel - Controller.GetNaturalGravity();

			_range = rP.Length();

			#region main-final
			if (_selOfsPt && _range < OFS_D)
			{
				int h = tgt.HitPoints?.Count ?? 0;
				_selOfsPt = false;

				if (h > 0) _ofs = tgt.HitPoints[_p.RNG.Next(h)];
				else _ofs = new Offset
				{
					Frame = tgt.Frame,
					Hit = Vector3D.TransformNormal(tgt.Hit - tgt.Center, MatrixD.Transpose(tgt.Matrix))
				};
			}

			if (_range < FUSE_D)
			{
				if (_arm)
				{
					_arm = !_arm;
					foreach (var w in _warhead)
						w.IsArmed = true;
				}
				else if (!_cams && _range < Math.Max(tgt.Radius * PROX_R, KILL_D)) _kill = true;
				else if (!_kill)
				{
					var rDst = KILL_D + rV.Length() * Lib.TPS;

					for (int i = _sensors.Count; --i >= 0;)
					{
						var s = _sensors[i];

						if (s.Closed || !s.IsFunctional)
							_sensors.RemoveAtFast(i);
						else if (s.CanScan(FUSE_D))
						{
							var m = s.WorldMatrix;
							var dot = m.Forward.Dot(icpt + rV * Lib.TPS);

							if (dot < CAM_ANG) continue;

							var info = s.Raycast(rDst);
							_p.Debug.DrawLine(m.Translation, m.Translation + m.Forward * rDst, Color.Coral);

							if (!info.IsEmpty())
							{
								_kill |= (info.HitPosition.Value - _pos).Length() < KILL_D;
								break;
							}
						}
					}
				}

				if (_kill)
				{
					if (_warhead.Count > 0)
					{
						var w = _warhead[0];
						w.Detonate();
						_warhead.RemoveAtFast(0);
					}
					else
					{
						_dead = true;
						NextStatusF = NextUpdateF;
					}
				}
			}
			#endregion

			#region main-icpt
			if (_checkAccel)
			{
				_accel = 0;
				foreach (var th in _thrust)
					_accel += th.MaxEffectiveThrust;
				_accel /= Controller.CalculateShipMass().TotalMass;
			}

			double
				a = 0.25 * rA.LengthSquared() - (_accel * _accel),
				b = rA.Dot(rV),
				c = rA.Dot(rP) + rV.LengthSquared(),
				d = 2 * rP.Dot(rV),
				e = rV.LengthSquared(),
				t = FastSolver.Solve(a, b, c, d, e);

			if (t == double.MaxValue || double.IsNaN(t)) t = MAX_TTK;
			else t = Math.Min(t, MAX_TTK) + tgt.Elapsed(_p.F);

			icpt += (rV * t) + (0.5 * rA * t * t);
			_rSq = (icpt - _pos).LengthSquared();

			_evade = _evade && _rSq < _evMaxSq && _rSq > _evMinSq;

			if (_evade && _p.F >= NextEvnAdjF)
			{
				var invlerp = Lib.InvLerp(_rSq, _evMaxSq, _evMinSq);

				_p.RandomNormalVector(ref rP, ref _evn);
				icpt += _evn * Lib.Lerp(invlerp, tgt.Radius * 2, tgt.Radius);
				NextEvnAdjF += Convert.ToInt64(Lib.Lerp(invlerp, ADJ_MAX, ADJ_MIN));
			}

			_cmd = Vector3D.TransformNormal(icpt - _pos, ref _viewMat);

			AimGyro();

			if (tgt.PriorityKill || _p.MaxMslSpeed - vel.Length() > 5) return;

			bool cr = !_evade && vel.Dot(_cmd) > 0.995;
			if (cr || (!cr && _thrust[0].ThrustOverridePercentage < 1))
				foreach (var th in _thrust)
					th.ThrustOverridePercentage = cr ? 0.5f : 1;

			#endregion
		}
	}
}