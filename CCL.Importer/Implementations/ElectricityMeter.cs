using System.Collections.Generic;

using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

using DV.JObjectExtstensions;
using DV.ServicePenalty;
using DV.Simulation.Cars;
using DV.ThingTypes;
using DV.Utils;
using LocoSim.Implementations;

using CCL.Importer.Components.Simulation.Electric;

namespace CCL.Importer.Implementations
{
	[HarmonyPatch(typeof(SimulatedCarDebtTracker))]
	public class ElectricityMeter : SimComponent
	{
		[HarmonyPatch(typeof(SimController), "OnLogicCarInitialized")]
		private static class LogicCarInitializer
		{
			public static void Postfix(SimController __instance)
			{
				foreach (ElectricityMeter meter in _metersWaitingForSetup)
					meter.SetupFeeTracker();
				_metersWaitingForSetup.ExceptWith(_metersFullySetUp);
				_metersFullySetUp.Clear();
			}
		}
		
		private static readonly Dictionary<SimulatedCarDebtTracker, ElectricityMeter> _feeTrackers = new();
		
		private static readonly HashSet<        TrainCar> _carsWithMeters     = new();
		private static readonly HashSet<ElectricityMeter> _metersWaitingForSetup = new(), _metersFullySetUp = new();
		
		private readonly TrainCar?                _unit                    = null;
		private          SimulatedCarDebtTracker? _feeTracker              = null;
		
		private readonly FuseReference? _masterFuse = null;
		private readonly Port           _electricChargeConsumed;
		private readonly PortReference  _supplyVoltage, _currentDraw;

		private float  _energyConsumptionFactor;
		private double _energyConsumed = 0.0;

		public override bool HasSaveData => true;
		
		public ElectricityMeter(ElectricityMeterDefinitionInternal definition) : base(definition.ID)
		{
			_energyConsumptionFactor = definition.electricChargeConsumptionFactor / (1000.0f * 3600.0f);

			if (!string.IsNullOrEmpty(definition.masterControlFuseId))
				_masterFuse = AddFuseReference(definition.masterControlFuseId);
			_electricChargeConsumed = AddPort(definition.electricChargeConsumed);
			_supplyVoltage          = AddPortReference(definition.supplyVoltage);
			_currentDraw            = AddPortReference(definition.currentDraw  );

			_unit = TrainCar.Resolve(definition.gameObject);
			if (_unit == null)
			{
				CCLPlugin.Error("Train car not found - electricity meter disabled");
				return;
			}
			if (_carsWithMeters.Contains(_unit))
			{
				CCLPlugin.Error("Another electricity meter present on the car - duplicate meters disabled");
				return;
			}
			_carsWithMeters.Add(_unit);
			_metersWaitingForSetup.Add(this);
			if (gameParams == null)
				_unit.LogicCarInitialized += AdjustEnergyConsumptionFactor;
			else
				AdjustEnergyConsumptionFactor();
			_unit.OnCarAboutToBeDestroyed += DisposeFeeTracker;
		}

		private void AdjustEnergyConsumptionFactor()
		{ 
			if (_unit != null)
			{
				_unit.LogicCarInitialized -= AdjustEnergyConsumptionFactor;
				_energyConsumptionFactor  *= gameParams.ResourceConsumptionModifier;
			}
		}

		private void SetupFeeTracker()
		{
			LocoDebtController? allFees = SingletonBehaviour<LocoDebtController>.Instance;
			if (allFees?.trackedLocosDebts != null)
			{
				foreach (ExistingLocoDebt? trackedFee in allFees.trackedLocosDebts)
				{
					if (trackedFee != null && trackedFee.car == _unit 
						&& trackedFee.locoDebtTracker is SimulatedCarDebtTracker feeTracker)
					{
						CCLPlugin.LogVerbose($"Set up a fee tracker {trackedFee.ID} for car {_unit.ID}");
						_feeTracker              = feeTracker;
						_feeTrackers[feeTracker] = this;
						_metersFullySetUp.Add(this);
						feeTracker.UpdateDebtValues();
						return;
					}
				}
			}
		}

		private void DisposeFeeTracker()
		{
			if (_unit == null)
				return;
			_metersWaitingForSetup.Remove(this);
			_carsWithMeters.Remove(_unit);
			if (_feeTracker != null && _feeTrackers.ContainsKey(_feeTracker))
			{
				_feeTrackers.Remove(_feeTracker);
				_feeTracker = null;
			}
			CCLPlugin.LogVerbose($"Removed fee tracker for car {_unit.ID}");
		}

		public override void Tick(float delta)
		{
			float load = _currentDraw.Value, voltage = _supplyVoltage.Value;
			if (load != 0.0f && !float.IsNaN(   load) && !float.IsInfinity(   load)  
				             && !float.IsNaN(voltage) && !float.IsInfinity(voltage) && _feeTracker != null)
			{
				_energyConsumed              += load * voltage * _energyConsumptionFactor * delta;
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
				_electricChargeConsumed.Value = (float) _energyConsumed;
				_feeTracker?.UpdateDebtValues();
			}
		}

		[HarmonyPatch("UpdateDebtValues"), HarmonyPostfix]
		public static void UpdateDebtValuesPostfix(SimulatedCarDebtTracker? __instance)
		{
			if (__instance == null)
				return;
			foreach (DebtComponent currentFee in __instance.GetTrackedDebts())
			{
				if (currentFee.Type == ResourceType.ElectricCharge && _feeTrackers.TryGetValue(__instance, out ElectricityMeter meter))
				{ 
					currentFee.UpdateEndValue(currentFee.EndValue - Mathf.Max((float) meter._energyConsumed, 0.0f));
					break;
				}
			}
		}

		[HarmonyPatch("ResetState"), HarmonyPostfix]
		public static void ResetStatePostfix(SimulatedCarDebtTracker? __instance)
		{
			if (__instance != null && _feeTrackers.TryGetValue(__instance, out ElectricityMeter meter))
			{
				meter._energyConsumed               = 0.0;
				meter._electricChargeConsumed.Value = 0.0f;
				meter._masterFuse?.ChangeState(false);
			}
		}
	}
}
