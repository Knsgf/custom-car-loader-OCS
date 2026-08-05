using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

using LocoSim.Implementations;

using CCL.Importer.Components.Simulation.Electric;

namespace CCL.Importer.Implementations
{
	internal class Pantograph : SimComponent
	{
		const string OCSClassName                      = "electric_sim.catenary.overhead_equipment, electric_sim";
		const string OCSPropertyName                   = "system";
		const string OCSWireHeightAndVoltageMethodName = "relative_wire_height_and_voltage";
		const string OCSActivationEventName            = "catenary_activated";
		const string OCSDeactivationEventName          = "catenary_deactivated";

		const int initializationTimeOut = 3 * 60, retryTime = 5;

		private static readonly float _hugeHeight = Mathf.Sqrt(float.MaxValue / 2.0f);
		
		private static Type?         _OCSType                     = null;
		private static MethodInfo?   _getWireHeightAndVoltageInfo = null;
		private static PropertyInfo? _OCSObjectInfo               = null;

		private static Dictionary<TrainCar, List<Pantograph>> _allPantographs = new();
		private static Dictionary<TrainCar, int> _nextPantographID = new(), _raisedPantographMask = new(), _raisedPantographCount = new();
		
		private Func<Transform, Transform, Transform, Transform, float, (float?, float)>? GetWireHeightAndVoltage = null;
		
		private readonly FuseReference _masterFuse;
		private readonly Port          _voltageReadOut, _voltageNormalizedReadOut, _raiseReadOut, _raiseNormalizedReadOut;
		private readonly PortReference _pantographToggle, _pantographLoad;

		private readonly TrainCar?  _unit;
		private readonly Transform? _base, _stripEnd1, _stripEnd2;

		private readonly float _minimumRaise = 0.0f, _maximumRaise, _maximumRaiseDifference, _headMovementSpeed, _contactTolerance;
		private readonly int   _IDMask, _IDInvertedMask;
		
		private bool    _unitDestroyed       = false;
		private Vector3 _lastStripEndPosition = new Vector3(0.0f, _hugeHeight, 0.0f);
		private float   _lastStripMidpointHeight;

		private static async void TryGetOCSType()
		{
			for (int remainingTime = initializationTimeOut; remainingTime >= 0; remainingTime -= retryTime)
			{
				_OCSType = Type.GetType(OCSClassName, throwOnError: false);
				if (_OCSType != null)
					break;
				Debug.Log($"CCL PNT RT={remainingTime}");
				await Task.Delay(retryTime * 1000);
			}
			Debug.Log($"CCL PNT OCS {_OCSType?.ToString() ?? "NULL"}");
			
			if (_OCSType != null)
			{
				_getWireHeightAndVoltageInfo = _OCSType.GetMethod(OCSWireHeightAndVoltageMethodName, 
					new Type[] { typeof(Transform), typeof(Transform), typeof(Transform), typeof(Transform), typeof(float) });
				Debug.Log($"CCL PNT GWHV {_getWireHeightAndVoltageInfo?.ToString() ?? "NULL"}");
				_OCSObjectInfo = _OCSType.GetProperty(OCSPropertyName, BindingFlags.Public | BindingFlags.Static);
				Debug.Log($"CCL PNT SYS {_OCSObjectInfo?.ToString() ?? "NULL"}");
				EventInfo? OCSActivationInfo   = _OCSType.GetEvent(  OCSActivationEventName, BindingFlags.Public | BindingFlags.Static);
				EventInfo? OCSDeactivationInfo = _OCSType.GetEvent(OCSDeactivationEventName, BindingFlags.Public | BindingFlags.Static);
				if (OCSActivationInfo == null || OCSDeactivationInfo == null)
				{
					_OCSType = null;
					return;
				}
				Debug.Log($"CCL PNT OCS+ {OCSActivationInfo}");
				Debug.Log($"CCL PNT OCS- {OCSDeactivationInfo}");
				OCSActivationInfo.AddEventHandler  (null, (Action) SetUpConnectionForAllPantographs);
				OCSDeactivationInfo.AddEventHandler(null, (Action) SeverConnectionForAllPantographs);
				SetUpConnectionForAllPantographs();
			}
		}

		private static void SetUpConnectionForAllPantographs()
		{
			Debug.Log("OCS+");
			if (_OCSType != null)
			{
				foreach (List<Pantograph> currentCarPantographs in _allPantographs.Values)
				{
					foreach (Pantograph currentPantograph in currentCarPantographs)
						currentPantograph.SetUpCatenaryConnection();
				}
			}
		}
		
		private static void SeverConnectionForAllPantographs()
		{
			Debug.Log("OCS-");
			if (_OCSType != null)
			{
				foreach (List<Pantograph> currentCarPantographs in _allPantographs.Values)
				{
					foreach (Pantograph currentPantograph in currentCarPantographs)
						currentPantograph.GetWireHeightAndVoltage = null;
				}
			}
		}
		
		static Pantograph()
		{
			TryGetOCSType();
		}

		private float StripMidpointHeight(TrainCar unit, Transform stripEnd1, Transform stripEnd2)
		{
			Vector3 currentStripEndPosition = stripEnd1.position;
			Vector3 positionDifference      = currentStripEndPosition - _lastStripEndPosition;
			if (   Math.Abs(positionDifference.x) + Math.Abs(positionDifference.z) > 0.09f
				|| Math.Abs(positionDifference.y) > 0.003f)
			{
				_lastStripEndPosition    = currentStripEndPosition;
				_lastStripMidpointHeight = unit.transform.InverseTransformPoint((currentStripEndPosition + stripEnd2.position) / 2.0f).y;
			}
			return _lastStripMidpointHeight;
		}

		public Pantograph(PantographDefinitionInternal definition): base(definition.ID)
		{
			_base              = definition.pantographBase;
			_stripEnd1         = definition.contactStripFirstEnd;
			_stripEnd2         = definition.contactStripSecondEnd;
			_headMovementSpeed = definition.headMovementSpeed;
			_maximumRaise      = definition.maximumRaise;
			_contactTolerance  = definition.contactTolerance;

			_masterFuse               = AddFuseReference(definition.masterControlFuseId);
			_voltageReadOut           = AddPort(definition.supplyVoltage            );
			_voltageNormalizedReadOut = AddPort(definition.supplyVoltageNormalized  );
			_raiseReadOut             = AddPort(definition.pantographRaise          );
			_raiseNormalizedReadOut   = AddPort(definition.pantographRaiseNormalized);
			_pantographToggle         = AddPortReference(definition.toggle     );
			_pantographLoad           = AddPortReference(definition.currentDraw);

			_unit = TrainCar.Resolve(definition.pantographBase);
			Debug.Log($"CCL PNT <{_unit?.name ?? "NULL"}>");
			TrainCar? unit = _unit;
			if (unit == null)
				return;
			if (_stripEnd1 != null && _stripEnd2 != null)
				_raiseReadOut.Value = _minimumRaise = StripMidpointHeight(unit, _stripEnd1, _stripEnd2);
			if (_maximumRaise < _minimumRaise + 0.001f)
				_maximumRaise = _minimumRaise + 0.001f;
			_maximumRaiseDifference = _maximumRaise - _minimumRaise;
			Debug.Log($"CCL PNT {_minimumRaise} {definition.maximumRaise} {_maximumRaiseDifference} {definition.headMovementSpeed}");

			if (_allPantographs.TryGetValue(unit, out List<Pantograph> installedPantographs))
			{
				_IDMask = 1 << _nextPantographID[unit]++;
				installedPantographs.Add(this);
			}
			else
			{
				_IDMask = 1;
				_allPantographs      [unit]   = new() { this };
				_nextPantographID    [unit]   = 1;
				_raisedPantographMask[unit]   = _raisedPantographCount[unit] = 0;
				unit.OnCarAboutToBeDestroyed += OnCarDestroyed;
			}
			_IDInvertedMask = ~_IDMask;
			Debug.Log($"CCL PNT {_IDMask} {_IDInvertedMask} {_allPantographs[unit].Count} {_nextPantographID[unit]}");
			
			SetUpCatenaryConnection();
		}

		private void SetUpCatenaryConnection()
		{
			GetWireHeightAndVoltage = null;
			if (_unitDestroyed || _OCSType == null || _OCSObjectInfo == null || _getWireHeightAndVoltageInfo == null)
				return;

			object? OCSInstance;
			try
			{
				OCSInstance = _OCSObjectInfo.GetValue(null);
			}
			catch (InvalidOperationException _)
			{
				return;
			}
			Debug.Log($"CCL PNT SYSP {OCSInstance ?? "NULL"}");
			if (OCSInstance != null)
			{
				GetWireHeightAndVoltage = _getWireHeightAndVoltageInfo.CreateDelegate(typeof(Func<Transform, Transform, Transform, Transform, float, (float?, float)>), OCSInstance)
					as Func<Transform, Transform, Transform, Transform, float, (float?, float)>;
				Debug.Log($"CCL PNT DLG {GetWireHeightAndVoltage?.ToString() ?? "NULL"}");
			}
		}

		private void OnCarDestroyed()
		{
			TrainCar? unit = _unit;
			if (unit == null || !_allPantographs.ContainsKey(unit))
				return;
			unit.OnCarAboutToBeDestroyed -= OnCarDestroyed;
			foreach (Pantograph currentPantograph in _allPantographs[unit])
			{
				currentPantograph.GetWireHeightAndVoltage = null;
				currentPantograph._unitDestroyed          = true;
			}
			_allPantographs[unit].Clear();
			_allPantographs.Remove       (unit);
			_nextPantographID.Remove     (unit);
			_raisedPantographMask.Remove (unit);
			_raisedPantographCount.Remove(unit);
		}

		private bool trackContactState(float? wireHeight, bool pantographOn)
		{
			TrainCar? unit = _unit;
			if (_unitDestroyed || unit == null || _stripEnd1 == null || _stripEnd2 == null)
				return false;
			bool wasInContact = (_raisedPantographMask[unit] & _IDMask) != 0;
			bool nowInContact;
			if (!pantographOn || wireHeight == null)
				nowInContact = false;
			else
				nowInContact = Mathf.Abs((float) wireHeight - StripMidpointHeight(unit, _stripEnd1, _stripEnd2)) <= _contactTolerance;
			if (nowInContact != wasInContact)
			{
				if (nowInContact)
				{
					_raisedPantographMask [unit] |= _IDMask;
					_raisedPantographCount[unit]++;
				}
				else
				{
					_raisedPantographMask [unit] &= _IDInvertedMask;
					_raisedPantographCount[unit]--;
				}
			}
			return nowInContact;
		}
		
		private void Move(float delta, float raiseHeight, bool pantographOn)
		{
			if (_unitDestroyed || _unit == null || _stripEnd1 == null || _stripEnd2 == null)
				return;
			float currentRaise = _raiseReadOut.Value;
			float targetRaise, raiseDifference;
			if (pantographOn)
			{
				targetRaise     = raiseHeight;
				raiseDifference = targetRaise - StripMidpointHeight(_unit, _stripEnd1, _stripEnd2);
			}
			else
			{
				targetRaise     = _minimumRaise;
				raiseDifference = targetRaise - currentRaise;
			}
			if (raiseDifference > 0.006f)
			{
				float movementSpeed           = Mathf.Min(_headMovementSpeed, Mathf.Abs(raiseDifference) / 0.2f);
				currentRaise                  = Mathf.Min(currentRaise + movementSpeed * delta, _maximumRaise);
				_raiseReadOut.Value           = currentRaise;
				_raiseNormalizedReadOut.Value = Mathf.Clamp((currentRaise - _minimumRaise) / _maximumRaiseDifference, 0.0f, 0.999f);
			}
			else if (raiseDifference < -0.006f)
			{
				float movementSpeed           = Mathf.Min(_headMovementSpeed, Mathf.Abs(raiseDifference) / 0.2f);
				currentRaise                  = Mathf.Max(currentRaise - movementSpeed * delta, _minimumRaise);
				_raiseReadOut.Value           = currentRaise;
				_raiseNormalizedReadOut.Value = Mathf.Clamp((currentRaise - _minimumRaise) / _maximumRaiseDifference, 0.0f, 0.999f);
			}
		}

		public override void Tick(float delta)
		{
			if (_unitDestroyed || _unit == null || _base == null || _stripEnd1 == null || _stripEnd2 == null)
				return;
			float? wireHeight;
			int    raisedPantographs = _raisedPantographCount[_unit];
			float  total_load        = _pantographLoad.Value;
			bool   pantographOn      = _pantographToggle.Value >= 0.5f && _masterFuse.State;
			float  load              = (raisedPantographs == 0 || float.IsNaN(total_load) || float.IsInfinity(total_load))
									 ? 0.0f : (total_load / raisedPantographs);
			float  voltage;
			if (pantographOn && GetWireHeightAndVoltage != null)
				(wireHeight, voltage) = GetWireHeightAndVoltage(_unit.transform, _base, _stripEnd1, _stripEnd2, load);
			else
			{
				wireHeight = null;
				voltage    = 0.0f;
			}
			float raiseHeight = !pantographOn ? _minimumRaise : (wireHeight ?? _maximumRaise);
			Move(delta, raiseHeight, pantographOn);
			if (!trackContactState(wireHeight, pantographOn))
				_voltageReadOut.Value = _voltageNormalizedReadOut.Value = 0.0f;
			else
			{
				_voltageReadOut.Value           = voltage;
				_voltageNormalizedReadOut.Value = voltage / 1500.0f;
			}
		}
	}
}
