using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using LocoSim.Implementations;

using CCL.Importer.Components.Simulation.Electric;

namespace CCL.Importer.Implementations
{
	internal class Pantograph : SimComponent
	{
		const string OCSClassName    = "electric_sim.catenary.overhead_equipment, electric_sim";
		const string OCSPropertyName = "system";
		const string OCSWireHeightAndVoltageMethodName = "relative_wire_height_and_voltage";
		const string OCSActivationEventName = "catenary_activated", OCSDeactivationEventName = "catenary_deactivated";

		const int initializationTimeOut = 10 * 60, retryTime = 5;

		private static Type?         _OCSType                     = null;
		private static MethodInfo?   _getWireHeightAndVoltageInfo = null;
		private static PropertyInfo? _OCSObjectInfo               = null;

		private static Dictionary<TrainCar, List<Pantograph>> _allPantographs = new();
		private static Dictionary<TrainCar, int> _nextPantographID = new(), _raisedPantographMask = new(), _raisedPantographCount = new();
		
		private Func<Transform, Transform, Transform, Transform, float, (float?, float)>? GetWireHeightAndVoltage;
		
		private readonly Port _voltageReadOut, _voltageNormalizedReadOut, _heightReadOut, _heightNormalizedReadOut;
		private readonly PortReference _pantographLoad;

		private readonly TrainCar? _unit;
		private readonly Transform? _base, _stripEnd1, _stripEnd2;

		private readonly float _maximumRaise;
		private readonly int   _ID;
		
		private bool _unitDestroyed = false;

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

		public Pantograph(PantographDefinitionInternal definition): base(definition.ID)
		{
			_voltageReadOut = AddPort(definition.supplyVoltage);
			_voltageNormalizedReadOut = AddPort(definition.supplyVoltageNormalized);
			_heightReadOut = AddPort(definition.pantographRaise);
			_heightNormalizedReadOut = AddPort(definition.pantographRaiseNormalized);

			_pantographLoad = AddPortReference(definition.currentDraw);
			Debug.Log($"CCL PNT {definition.maximumRaise} {definition.headMovementSpeed}");
			_unit = TrainCar.Resolve(definition.pantographBase);
			Debug.Log($"CCL PNT <{_unit?.name ?? "NULL"}>");

			TrainCar? unit = _unit;
			if (unit == null)
				return;
			if (_allPantographs.TryGetValue(unit, out List<Pantograph> installedPantographs))
			{
				_ID = _nextPantographID[unit]++;
				installedPantographs.Add(this);
			}
			else
			{
				_ID = 0;
				_allPantographs      [unit]   = new() { this };
				_nextPantographID    [unit]   = 1;
				_raisedPantographMask[unit]   = _raisedPantographCount[unit] = 0;
				unit.OnCarAboutToBeDestroyed += OnCarDestroyed;
			}
			
			_base = definition.pantographBase;
			_stripEnd1 = definition.contactStripFirstEnd;
			_stripEnd2 = definition.contactStripSecondEnd;
			_maximumRaise = definition.maximumRaise;

			SetUpCatenaryConnection();
		}

		private void SetUpCatenaryConnection()
		{
			GetWireHeightAndVoltage = null;
			if (_unitDestroyed || _OCSType == null || _OCSObjectInfo == null || _getWireHeightAndVoltageInfo == null)
				return;

			object OCSInstance;
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

		public override void Tick(float delta)
		{
			if (_unit == null || _base == null || _stripEnd1 == null || _stripEnd2 == null)
				return;
			float? contactHeight;
			float  voltage;
			if (GetWireHeightAndVoltage != null)
				(contactHeight, voltage) = GetWireHeightAndVoltage(_unit.transform, _base, _stripEnd1, _stripEnd2, 0.0f);
			else
			{
				contactHeight = null;
				voltage       = 0.0f;
			}
			if (contactHeight == null)
			{
				_voltageReadOut.Value = _voltageNormalizedReadOut.Value = 0.0f;
				_heightReadOut.Value = _maximumRaise;
			}
			else
			{
				_voltageReadOut.Value = voltage;
				_voltageNormalizedReadOut.Value = voltage / 1500.0f;
				_heightReadOut.Value = (float) contactHeight;
			}
		}
	}
}
