using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CCL.Importer.Implementations;

using LocoSim.Definitions;
using LocoSim.Implementations;

using UnityEngine;

namespace CCL.Importer.Components.Simulation.Electric
{
	internal class PantographDefinitionInternal : SimComponentDefinition
	{
		public Transform? ContactStripFirstEnd, ContactStripSecondEnd;
		public float maximumRaise;
		public bool alwaysExtendFully;

		public string masterControlFuseId = string.Empty;
		public string pantographToggleId  = string.Empty;

		public readonly PortDefinition supplyVoltage             = new(PortType.READONLY_OUT, PortValueType.VOLTS  , "VOLTAGE"                    );
		public readonly PortDefinition supplyVoltageNormalized   = new(PortType.READONLY_OUT, PortValueType.STATE  , "VOLTAGE_NORMALIZED"         );
		public readonly PortDefinition pantographRaise           = new(PortType.READONLY_OUT, PortValueType.GENERIC, "PANTOGRAPH_RAISE"           );
		public readonly PortDefinition pantographRaiseNormalized = new(PortType.READONLY_OUT, PortValueType.STATE  , "PANTOGRAPH_RAISE_NORMALIZED");

		public readonly PortReferenceDefinition currentDraw = new(PortValueType.AMPS, "CURRENT_DRAW", writeAllowed: false);

		
		public override SimComponent InstantiateImplementation() => new Pantograph(this);
	}
}
