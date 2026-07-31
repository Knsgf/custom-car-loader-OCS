using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using LocoSim.Implementations;

using CCL.Importer.Components.Simulation.Electric;

namespace CCL.Importer.Implementations
{
	internal class ElTest : SimComponent
	{
		private readonly float _f;
		private float _t = 0.0f;
		
		public ElTest(ElTestDefinitionInternal def): base(def.ID)
		{
			_f = def.f;
		}

		public override void Tick(float delta)
		{
			_t += delta;
			if (_t >= _f)
			{
				Debug.Log($"ElTest {_t}>={_f}");
				_t -= _f;
			}
		}
	}
}
