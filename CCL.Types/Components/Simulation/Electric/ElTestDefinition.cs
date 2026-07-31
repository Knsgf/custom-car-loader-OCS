using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using CCL.Types.Proxies.Ports;

namespace CCL.Types.Components.Simulation.Electric
{
	[AddComponentMenu("CCL/Components/Simulation/Electric/ElTest")]
	public class ElTestDefinition : SimComponentDefinitionProxy
	{
		[Min(1.0f)]
		public float f;
	}
}
