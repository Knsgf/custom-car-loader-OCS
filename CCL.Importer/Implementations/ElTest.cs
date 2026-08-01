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
		//const string OCS_class_name = "electric_sim.catenary.overhead_equipment, electric_sim, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
		const string OCS_class_name = "electric_sim.catenary.overhead_equipment, electric_sim";
		
		private readonly float _f;
		private float _t = 0.0f;
		
		private void test_load()
		{
			var t = Type.GetType(OCS_class_name, throwOnError: false);
			Debug.Log($"ElTest {((t == null) ? "NULL" : t)}");
		}
		
		public ElTest(ElTestDefinitionInternal def): base(def.ID)
		{
			_f = def.f;
			test_load();
		}

		public override void Tick(float delta)
		{
			_t += delta;
			if (_t >= _f)
			{
				Debug.Log($"ElTest {_t}>={_f}");
				_t -= _f;
				test_load();
			}
		}
	}
}
