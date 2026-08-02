using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using CCL.Types.Proxies.Ports;

namespace CCL.Types.Components.Simulation.Electric
{
	[AddComponentMenu("CCL/Components/Simulation/Electric/Pantograph Definition")]
	public class PantographDefinition : SimComponentDefinitionProxy, IHasFuseIdFields
	{
		public Transform? pantographBase;
		public Transform? contactStripFirstEnd, contactStripSecondEnd;
		
		[Min(0.0f), Tooltip("Maximum reach height above zero. The minimum height is taken from initial position")]
		public float maximumRaise;
		
		[Min(0.0f)]
		public float headMovementSpeed;
		
		[Tooltip("When unchecked, the pantograph extends till it makes contact. If checked, extends all the way ignoring contact; useful for some side trolley designs")]
		public bool alwaysExtendFully;

		[FuseId(true)]
		public string masterControlFuseId = string.Empty;
		[FuseId(true)]
		public string pantographToggleId = string.Empty;

		public override IEnumerable<PortDefinition> ExposedPorts => new[]
		{
			new PortDefinition(DVPortType.READONLY_OUT, DVPortValueType.VOLTS  , "VOLTAGE"                    ),
			new PortDefinition(DVPortType.READONLY_OUT, DVPortValueType.STATE  , "VOLTAGE_NORMALIZED"         ),
			new PortDefinition(DVPortType.READONLY_OUT, DVPortValueType.GENERIC, "PANTOGRAPH_RAISE"           ),
			new PortDefinition(DVPortType.READONLY_OUT, DVPortValueType.STATE  , "PANTOGRAPH_RAISE_NORMALIZED")
		};

		public override IEnumerable<PortReferenceDefinition> ExposedPortReferences => new[]
		{
			new PortReferenceDefinition(DVPortValueType.AMPS,"CURRENT_DRAW", writeAllowed: false)
		};

		public IEnumerable<FuseIdField> ExposedFuseIdFields => new[]
		{
			new FuseIdField(this, nameof(masterControlFuseId), masterControlFuseId, required: true),
			new FuseIdField(this, nameof(pantographToggleId ),  pantographToggleId, required: true)
		};
	}
}
