using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CCL.Importer.Implementations;

using LocoSim.Definitions;
using LocoSim.Implementations;

namespace CCL.Importer.Components.Simulation.Electric
{
	internal class ElTestDefinitionInternal : SimComponentDefinition
	{
		public float f;
		
		public override SimComponent InstantiateImplementation() => new ElTest(this);
	}
}
