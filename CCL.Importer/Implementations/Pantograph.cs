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
	internal class Pantograph : SimComponent
	{
		//const string OCS_class_name = "electric_sim.catenary.overhead_equipment, electric_sim, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
		const string OCS_class_name = "electric_sim.catenary.overhead_equipment, electric_sim";
		
		private void test_load()
		{
			var t = Type.GetType(OCS_class_name, throwOnError: false);
			Debug.Log($"CCL PNT {((t == null) ? "NULL" : t)}");
		}
		
		public Pantograph(PantographDefinitionInternal def): base(def.ID)
		{
			test_load();
			Debug.Log($"CCL PNT {def.maximumRaise}");
		}

		public override void Tick(float delta)
		{
		}
	}
}
