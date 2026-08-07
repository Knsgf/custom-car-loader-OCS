using UnityEngine;

using LocoSim.Definitions;
using LocoSim.Implementations;

using CCL.Importer.Implementations;

namespace CCL.Importer.Components.Simulation.Electric
{
	internal class PantographDefinitionInternal : SimComponentDefinition
	{
		public Transform? pantographBase;
		public Transform? contactStripFirstEnd, contactStripSecondEnd;
		public float      nominalVoltage, maximumRaise, headMovementSpeed, contactTolerance, electricChargeConsumptionFactor;

		public string masterControlFuseId = string.Empty;

		public readonly PortDefinition supplyVoltage             = new(PortType.READONLY_OUT, PortValueType.VOLTS  , "VOLTAGE"                    );
		public readonly PortDefinition supplyVoltageNormalized   = new(PortType.READONLY_OUT, PortValueType.VOLTS  , "VOLTAGE_NORMALIZED"         );
		public readonly PortDefinition pantographRaise           = new(PortType.READONLY_OUT, PortValueType.GENERIC, "PANTOGRAPH_RAISE"           );
		public readonly PortDefinition pantographRaiseNormalized = new(PortType.READONLY_OUT, PortValueType.STATE  , "PANTOGRAPH_RAISE_NORMALIZED");

		public readonly PortReferenceDefinition toggle             = new(PortValueType.CONTROL        ,              "TOGGLE", writeAllowed: true );
		public readonly PortReferenceDefinition currentDraw        = new(PortValueType.AMPS           ,        "CURRENT_DRAW", writeAllowed: false);
		public readonly PortReferenceDefinition chargeConsumption  = new(PortValueType.ELECTRIC_CHARGE,  "CHARGE_CONSUMPTION", writeAllowed: true );
		public readonly PortReferenceDefinition chargeRegeneration = new(PortValueType.ELECTRIC_CHARGE, "CHARGE_REGENERATION", writeAllowed: true );

		public override SimComponent InstantiateImplementation() => new Pantograph(this);
	}
}
