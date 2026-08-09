using System.Collections.Generic;
using System.Threading.Tasks;

using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

using DV.JObjectExtstensions;
using DV.ServicePenalty;
using DV.ThingTypes;
using DV.Utils;
using LocoSim.Implementations;

using CCL.Importer.Components.Simulation.Electric;

namespace CCL.Importer.Implementations
{
	[HarmonyPatch(typeof(SimulatedCarDebtTracker))]
	public class ElectricityMeter : SimComponent
	{
		private static readonly Dictionary<SimulatedCarDebtTracker, ElectricityMeter> _feeTrackers = new();
		private static readonly HashSet<TrainCar> _carsWithMeters = new();
		
		private readonly TrainCar?                _unit                    = null;
		private          SimulatedCarDebtTracker? _feeTracker              = null;
		private readonly object                   _initializationInterlock = new();

		private readonly Port          _electricChargeConsumed;
		private readonly PortReference _supplyVoltage, _currentDraw;

		private float  _energyConsumptionFactor;
		private double _energyConsumed = 0.0;

		public override bool HasSaveData => true;
		
		public ElectricityMeter(ElectricityMeterDefinitionInternal definition) : base(definition.ID)
		{
			_energyConsumptionFactor = definition.electricChargeConsumptionFactor / (1000.0f * 3600.0f);
			Debug.Log($"CCL EMTR {_energyConsumptionFactor}");

			_electricChargeConsumed = AddPort(definition.electricChargeConsumed);
			_supplyVoltage          = AddPortReference(definition.supplyVoltage);
			_currentDraw            = AddPortReference(definition.currentDraw  );

			_unit = TrainCar.Resolve(definition.gameObject);
			if (_unit == null)
			{
				Debug.Log("CCL EMTR NGO");
				return;
			}
			if (gameParams == null)
				_unit.LogicCarInitialized += AdjustEnergyConsumptionFactor;
			else
				AdjustEnergyConsumptionFactor();
			//SetupFeeTracker();
			_unit.OnCarAboutToBeDestroyed += DisposeFeeTracker;
		}

		private void AdjustEnergyConsumptionFactor()
		{ 
			Debug.Log("CCL EMTR AECF");
			if (_unit != null)
			{
				_unit.LogicCarInitialized -= AdjustEnergyConsumptionFactor;
				_energyConsumptionFactor  *= gameParams.ResourceConsumptionModifier;
				Debug.Log($"CCL EMTR AECF {gameParams.ResourceConsumptionModifier}");
			}
		}

		private async void SetupFeeTracker()
		{
			if (_unit == null || _carsWithMeters.Contains(_unit))
			{
				Debug.Log($"CCL EMTR N/2+");
				return;
			}
			_carsWithMeters.Add(_unit);
			while (true)
			{
				LocoDebtController? allFees = SingletonBehaviour<LocoDebtController>.Instance;
				lock (_initializationInterlock)
				{
					if (!_carsWithMeters.Contains(_unit))
						return;
					if (allFees?.trackedLocosDebts != null)
					{
						foreach (ExistingLocoDebt? trackedFee in allFees.trackedLocosDebts)
						{
							if (trackedFee != null && trackedFee.car == _unit && trackedFee.locoDebtTracker is SimulatedCarDebtTracker feeTracker)
							{
								Debug.Log($"CCL EMTR FTRK {trackedFee.ID}");
								_feeTracker              = feeTracker;
								_feeTrackers[feeTracker] = this;
								feeTracker.UpdateDebtValues();
								return;
							}
						}
					}
				}
				Debug.Log($"CCL EMTR FTRK-");
				await Task.Delay(100);
			}
		}

		private void DisposeFeeTracker()
		{
			if (_unit == null)
				return;
			lock (_initializationInterlock)
			{
				_carsWithMeters.Remove(_unit);
				if (_feeTracker != null && _feeTrackers.ContainsKey(_feeTracker))
				{
					_feeTrackers.Remove(_feeTracker);
					_feeTracker = null;
				}
			}
		}

		public override void InitializationAfterConnecting()
		{
			Debug.Log("CCL EMTR IAC");
			SetupFeeTracker();
		}
		
		public override void Tick(float delta)
		{
			float load = _currentDraw.Value, voltage = _supplyVoltage.Value;
			if (load != 0.0f && !float.IsNaN(   load) && !float.IsInfinity(   load)  
				             && !float.IsNaN(voltage) && !float.IsInfinity(voltage) && _feeTracker != null)
			{
				_energyConsumed              += load * _supplyVoltage.Value * _energyConsumptionFactor * delta;
				_electricChargeConsumed.Value = (float) _energyConsumed;
			}
		}

		public override JObject? GetSaveStateData()
		{
			if (_feeTracker == null)
				return null;
			JObject savedData = new();
			savedData.SetDouble("energyConsumed", _energyConsumed);
			return savedData;
		}

		public override void SetSaveStateData(JObject? savedData)
		{
			if (savedData != null)
			{
				_energyConsumed = savedData.GetDouble("energyConsumed") ?? 0.0;
				if (double.IsNaN(_energyConsumed) || double.IsInfinity(_energyConsumed))
					_energyConsumed = 0.0;
				Debug.Log($"CCL EMTR SAVLD {_energyConsumed}");
				_feeTracker?.UpdateDebtValues();
			}
		}
	
		[HarmonyPatch("UpdateDebtValues"), HarmonyPostfix]
		public static void UpdateDebtValuesPostfix(SimulatedCarDebtTracker? __instance)
		{
			Debug.Log($"CCL EMTR UDV {__instance != null}");
			if (__instance == null)
				return;
			foreach (DebtComponent currentFee in __instance.GetTrackedDebts())
			{
				if (currentFee.Type == ResourceType.ElectricCharge && _feeTrackers.TryGetValue(__instance, out ElectricityMeter meter))
				{ 
					currentFee.UpdateEndValue(currentFee.EndValue - (float) meter._energyConsumed);
					Debug.Log($"CCL EMTR UDV {currentFee.StartToEndDiff} {meter._unit?.ID ?? "<null>"}");
					break;
				}
			}
		}

		[HarmonyPatch("ResetState"), HarmonyPostfix]
		public static void ResetStatePostfix(SimulatedCarDebtTracker? __instance)
		{
			Debug.Log($"CCL EMTR RFS {__instance != null}");
			if (__instance != null && _feeTrackers.TryGetValue(__instance, out ElectricityMeter meter))
			{
				Debug.Log($"CCL EMTR RFS {meter._unit?.ID ?? "<null>"}");
				meter._energyConsumed = 0.0;
			}
		}
	}
}
