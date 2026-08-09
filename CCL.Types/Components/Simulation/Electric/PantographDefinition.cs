using System.Collections.Generic;
using UnityEngine;

using CCL.Types.Proxies.Ports;

namespace CCL.Types.Components.Simulation.Electric
{
	[AddComponentMenu("CCL/Components/Simulation/Electric/Pantograph Definition")]
	public class PantographDefinition : SimComponentDefinitionProxy, IHasFuseIdFields
	{
		public Transform? pantographBase;
		public Transform? contactStripFirstEnd, contactStripSecondEnd;
		
		[Min(1.0f), Tooltip("Used to calculate normalized voltage port")]
		public float nominalVoltage = 1500.0f;
		
		[Min(0.01f), Tooltip("Pantograph head movement speed in m/s")]
		public float headMovementSpeed = 1.0f;

		[Min(0.0f), Tooltip("Maximum reach height. The minimum height is taken from initial position. Must match the reach from pantograph animation")]
		public float maximumRaise;

		[Min(0.01f), Tooltip("Maximum vertical offset between wire and strip midpoint for a contact to register")]
		public float contactTolerance = 0.2f;

		[FuseId(true)]
		public string masterControlFuseId = string.Empty;

		public override IEnumerable<PortDefinition> ExposedPorts => new[]
		{
			new PortDefinition(DVPortType.READONLY_OUT, DVPortValueType.VOLTS  , "VOLTAGE"                    ),
			new PortDefinition(DVPortType.READONLY_OUT, DVPortValueType.VOLTS  , "VOLTAGE_NORMALIZED"         ),
			new PortDefinition(DVPortType.READONLY_OUT, DVPortValueType.GENERIC, "PANTOGRAPH_RAISE"           ),
			new PortDefinition(DVPortType.READONLY_OUT, DVPortValueType.STATE  , "PANTOGRAPH_RAISE_NORMALIZED"),
		};

		public override IEnumerable<PortReferenceDefinition> ExposedPortReferences => new[]
		{
			new PortReferenceDefinition(DVPortValueType.CONTROL,       "TOGGLE", writeAllowed: true ),
			new PortReferenceDefinition(DVPortValueType.AMPS   , "CURRENT_DRAW", writeAllowed: false),
		};

		public IEnumerable<FuseIdField> ExposedFuseIdFields => new[]
		{
			new FuseIdField(this, nameof(masterControlFuseId), masterControlFuseId, required: true)
		};
	}
}
