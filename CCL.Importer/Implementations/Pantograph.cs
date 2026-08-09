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
		private static object?       _OCSInstance                 = null;

		private static readonly Dictionary<TrainCar, List<Pantograph>> _allPantographs = new();
		private static readonly Dictionary<TrainCar, int> _nextPantographID = new(), _raisedPantographMask = new(), _raisedPantographCount = new();
		
		private Func<Transform, Transform, Transform, Transform, float, (float?, float)>? GetWireHeightAndVoltage = null;
		
		private readonly FuseReference _masterFuse;
		private readonly Port          _voltageReadOut, _voltageNormalizedReadOut, _raiseReadOut, _raiseNormalizedReadOut;
		private readonly PortReference _pantographToggle, _pantographLoad;

		private readonly TrainCar?  _unit;
		private readonly Transform? _base, _stripEnd1, _stripEnd2;

		private readonly float _nominalVoltage;
		private readonly float _minimumRaise = 0.0f, _maximumRaise, _maximumRaiseDifference, _headMovementSpeed, _contactTolerance;
		private readonly int   _IDMask, _IDInvertedMask;
		
		private bool    _disabled             = false, _isInContact = false;
		private Vector3 _lastStripEndPosition = new Vector3(0.0f, _hugeHeight, 0.0f);
		private float   _lastStripMidpointHeight;

		private static async void TryGetOCSType()
		{
			for (int remainingTime = initializationTimeOut; remainingTime >= 0; remainingTime -= retryTime)
			{
				_OCSType = Type.GetType(OCSClassName, throwOnError: false);
				if (_OCSType != null)
					break;
				await Task.Delay(retryTime * 1000);
			}
			if (_OCSType == null)
			{
				CCLPlugin.Log("Catenary not installed; overhead power will be unavailable");
				return;
			}
			_getWireHeightAndVoltageInfo = _OCSType.GetMethod(OCSWireHeightAndVoltageMethodName, 
				new Type[] { typeof(Transform), typeof(Transform), typeof(Transform), typeof(Transform), typeof(float) });
			_OCSObjectInfo = _OCSType.GetProperty(OCSPropertyName, BindingFlags.Public | BindingFlags.Static);
			EventInfo? OCSActivationInfo   = _OCSType.GetEvent(  OCSActivationEventName, BindingFlags.Public | BindingFlags.Static);
			EventInfo? OCSDeactivationInfo = _OCSType.GetEvent(OCSDeactivationEventName, BindingFlags.Public | BindingFlags.Static);
			if (_getWireHeightAndVoltageInfo == null || _OCSObjectInfo == null || OCSActivationInfo == null || OCSDeactivationInfo == null)
			{
				CCLPlugin.Error("Unable to retreive OCS class information; overhead power will be unavailable");
				_OCSType = null;
				return;
			}
			OCSActivationInfo.AddEventHandler  (null, (Action) SetUpConnectionForAllPantographs);
			OCSDeactivationInfo.AddEventHandler(null, (Action) SeverConnectionForAllPantographs);
			SetUpConnectionForAllPantographs();
		}

		private static void SetUpConnectionForAllPantographs()
		{
			if (_OCSType != null && _OCSObjectInfo != null)
			{
				try
				{
					_OCSInstance = _OCSObjectInfo.GetValue(null);
				}
				catch (InvalidOperationException _)
				{
					CCLPlugin.Log("Catenary inactive, overhead power not available");
					_OCSInstance = null;
					return;
				}
				CCLPlugin.LogVerbose("Cantenary activated, restoring overhead power access");
				foreach (List<Pantograph> currentCarPantographs in _allPantographs.Values)
				{
					foreach (Pantograph currentPantograph in currentCarPantographs)
						currentPantograph.SetUpCatenaryConnection();
				}
			}
		}
		
		private static void SeverConnectionForAllPantographs()
		{
			CCLPlugin.LogVerbose("Cantenary deactivated, turning off overhead power");
			_OCSInstance = null;
			foreach (List<Pantograph> currentCarPantographs in _allPantographs.Values)
			{
				foreach (Pantograph currentPantograph in currentCarPantographs)
				{ 
					currentPantograph.GetWireHeightAndVoltage = null;
					currentPantograph._pantographToggle.Value = 0.0f;
				}
			}
		}
		
		static Pantograph()
		{
			TryGetOCSType();
		}

		private float GetStripMidpointHeight(TrainCar unit, Transform stripEnd1, Transform stripEnd2)
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
			_nominalVoltage    = definition.nominalVoltage;
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

			if (_nominalVoltage <= 0.0f)
			{
				CCLPlugin.Error("Nominal voltage negative or zero, pantograph disabled");
				_disabled = true;
				return;
			}
			if (_headMovementSpeed <= 0.0f)
			{
				CCLPlugin.Error("Head movement speed negative or zero, pantograph disabled");
				_disabled = true;
				return;
			}
			if (_base == null || _stripEnd1 == null || _stripEnd2 == null)
			{ 
				CCLPlugin.Error("Pantograph base or contact strip ends not specified, disabling");
				_disabled = true;
				return;
			}
			_unit = TrainCar.Resolve(definition.pantographBase);
			TrainCar? unit = _unit;
			if (unit == null)
			{
				CCLPlugin.Error("Car not found, pantograph disabled");
				_disabled = true;
				return;
			}
			
			_raiseReadOut.Value = _minimumRaise = GetStripMidpointHeight(unit, _stripEnd1, _stripEnd2);
			if (_maximumRaise <= _minimumRaise)
			{
				CCLPlugin.Error("Maximum reach is below initial position, pantograph disabled");
				_disabled = true;
				return;
			}
			_maximumRaiseDifference = _maximumRaise - _minimumRaise;
			if (_allPantographs.TryGetValue(unit, out List<Pantograph> installedPantographs))
			{
				if (_nextPantographID[unit] >= 30)
				{
					CCLPlugin.Error("Cannot have more than 30 pantographs on a car");
					_disabled = true;
					return;
				}
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
			SetUpCatenaryConnection();
		}

		private void SetUpCatenaryConnection()
		{
			GetWireHeightAndVoltage = null;
			if (_disabled || _OCSInstance == null || _getWireHeightAndVoltageInfo == null || _unit == null)
				return;
			GetWireHeightAndVoltage = _getWireHeightAndVoltageInfo.CreateDelegate(typeof(Func<Transform, Transform, Transform, Transform, float, (float?, float)>), _OCSInstance)
				as Func<Transform, Transform, Transform, Transform, float, (float?, float)>;
			if (GetWireHeightAndVoltage == null)
				CCLPlugin.Error($"Unable to connect car {_unit.name}, pantograph ID {_IDMask} to OCS, pantograph will not receive power");
			else
				CCLPlugin.LogVerbose($"Connection to OCS successfully established for car {_unit.name}, pantograph ID {_IDMask}");
		}

		private void OnCarDestroyed()
		{
			TrainCar? unit = _unit;
			if (unit == null || !_allPantographs.ContainsKey(unit))
				return;
			CCLPlugin.LogVerbose($"Removing OCS connections for car {unit.name} ({unit.ID})");
			unit.OnCarAboutToBeDestroyed -= OnCarDestroyed;
			foreach (Pantograph currentPantograph in _allPantographs[unit])
			{
				currentPantograph.GetWireHeightAndVoltage = null;
				currentPantograph._disabled               = true;
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
			if (_disabled || unit == null || _stripEnd1 == null || _stripEnd2 == null)
				return false;
			bool wasInContact = (_raisedPantographMask[unit] & _IDMask) != 0;
			bool nowInContact;
			if (!pantographOn || wireHeight == null)
				nowInContact = false;
			else
				nowInContact = Mathf.Abs((float) wireHeight - GetStripMidpointHeight(unit, _stripEnd1, _stripEnd2)) <= _contactTolerance;
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
			if (_disabled || _unit == null || _stripEnd1 == null || _stripEnd2 == null)
				return;
			float currentRaise = _raiseReadOut.Value;
			float targetRaise, raiseDifference;
			if (pantographOn)
			{
				targetRaise     = raiseHeight;
				raiseDifference = targetRaise - GetStripMidpointHeight(_unit, _stripEnd1, _stripEnd2);
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
			if (_disabled || _unit == null || _base == null || _stripEnd1 == null || _stripEnd2 == null)
				return;
			float? wireHeight;
			int    raisedPantographs = _raisedPantographCount[_unit];
			bool   pantographOn      = _pantographToggle.Value >= 0.5f && _masterFuse.State;
			float  load;
			if (!_isInContact || raisedPantographs == 0)
				load = 0.0f;
			else
			{
				float inputLoad = _pantographLoad.Value;
				load            = (float.IsNaN(inputLoad) || float.IsInfinity(inputLoad)) ? 0.0f : (inputLoad / raisedPantographs);
			}
			float voltage;
			if (pantographOn && GetWireHeightAndVoltage != null)
				(wireHeight, voltage) = GetWireHeightAndVoltage(_unit.transform, _base, _stripEnd1, _stripEnd2, load);
			else
			{
				wireHeight = null;
				voltage    = 0.0f;
			}
			float raiseHeight = !pantographOn ? _minimumRaise : (wireHeight ?? _maximumRaise);
			Move(delta, raiseHeight, pantographOn);
			_isInContact = trackContactState(wireHeight, pantographOn);
			if (!_isInContact)
				_voltageReadOut.Value = _voltageNormalizedReadOut.Value = 0.0f;
			else
			{
				_voltageReadOut.Value           = voltage;
				_voltageNormalizedReadOut.Value = voltage / _nominalVoltage;
			}
		}
	}
}
