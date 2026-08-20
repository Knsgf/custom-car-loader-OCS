using System.Collections.Generic;

using CCL.Types.Proxies.Ports;

using UnityEngine;

namespace CCL.Types.Components.Simulation.Electric
{
	[AddComponentMenu("CCL/Components/Simulation/Electric/Electricity Meter")]
	public class ElectricityMeterDefinition : SimComponentDefinitionProxy, IHasFuseIdFields
	{
		[Min(0.0f), Tooltip("Electric charge consumption multiplier for computing electricity fees. The default setting 0.6666667 is equivalent to $10/kWh")]
		public float electricChargeConsumptionFactor = 10.0f / 15.0f;

		[FuseId(false), Tooltip("Pantograph fuse to reset when fees are paid in Career Manager")]
		public string masterControlFuseId = string.Empty;

		public IEnumerable<FuseIdField> ExposedFuseIdFields => new[]
		{
			new FuseIdField(this, nameof(masterControlFuseId), masterControlFuseId, required: false)
		};

		public override IEnumerable<PortDefinition> ExposedPorts => new[]
		{
			new PortDefinition(DVPortType.READONLY_OUT, DVPortValueType.ELECTRIC_CHARGE, "ENERGY_CONSUMED")
		};

		public override IEnumerable<PortReferenceDefinition> ExposedPortReferences => new[]
		{
			new PortReferenceDefinition(DVPortValueType.VOLTS, "SUPPLY_VOLTAGE", writeAllowed: false),
			new PortReferenceDefinition(DVPortValueType.AMPS ,   "CURRENT_DRAW", writeAllowed: false)
		};
	}
}
