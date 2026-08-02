using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using LocoSim.Implementations;

using CCL.Importer.Components.Simulation.Electric;

namespace CCL.Importer.Implementations
{
	internal class Pantograph : SimComponent
	{
		const string OCSClassName    = "electric_sim.catenary.overhead_equipment, electric_sim";
		const string OCSPropertyName = "system";
		const string OCSWireHeightAndVoltageMethodName = "relative_wire_height_and_voltage";

		private Func<Transform, Transform, Transform, Transform, float, (float?, float)>? GetWireHeightAndVoltage;
		private object? _OCSObject;
		
		private readonly Port _voltageReadOut, _voltageNormalizedReadOut, _heightReadOut, _heightNormalizedReadOut;
		private readonly PortReference _pantographLoad;

		private readonly GameObject? _unit;
		private readonly Transform? _base, _stripEnd1, _stripEnd2;

		private readonly float _maximumRaise;

		public Pantograph(PantographDefinitionInternal def): base(def.ID)
		{
			Debug.Log($"CCL PNT {def.maximumRaise} {def.headMovementSpeed}");
			_unit = TrainCar.Resolve(def.pantographBase)?.gameObject;
			Debug.Log($"CCL PNT <{_unit?.name ?? "NULL"}>");

			var OCSType = Type.GetType(OCSClassName, throwOnError: false);
			if (OCSType != null)
			{
				MethodInfo getWireHeightAndVoltageInfo = OCSType.GetMethod(OCSWireHeightAndVoltageMethodName, 
					new Type[] { typeof(Transform), typeof(Transform), typeof(Transform), typeof(Transform), typeof(float) });
				Debug.Log($"CCL PNT GWHV {getWireHeightAndVoltageInfo?.ToString() ?? "NULL"}");
				PropertyInfo OCSObjectInfo = OCSType.GetProperty(OCSPropertyName, BindingFlags.Public | BindingFlags.Static);
				Debug.Log($"CCL PNT SYS {OCSObjectInfo?.ToString() ?? "NULL"}");
				if (OCSObjectInfo != null)
				{
					_OCSObject = OCSObjectInfo.GetValue(null);
					Debug.Log($"CCL PNT SYSP {_OCSObject ?? "NULL"}");
					if (_OCSObject != null && getWireHeightAndVoltageInfo != null)
					{
						GetWireHeightAndVoltage = getWireHeightAndVoltageInfo.CreateDelegate(typeof(Func<Transform, Transform, Transform, Transform, float, (float?, float)>), _OCSObject)
							as Func<Transform, Transform, Transform, Transform, float, (float?, float)>;
						Debug.Log($"CCL PNT DLG {GetWireHeightAndVoltage?.ToString() ?? "NULL"}");
					}
				}
			}

			_voltageReadOut = AddPort(def.supplyVoltage);
			_voltageNormalizedReadOut = AddPort(def.supplyVoltageNormalized);
			_heightReadOut = AddPort(def.pantographRaise);
			_heightNormalizedReadOut = AddPort(def.pantographRaiseNormalized);

			_pantographLoad = AddPortReference(def.currentDraw);

			_base = def.pantographBase;
			_stripEnd1 = def.contactStripFirstEnd;
			_stripEnd2 = def.contactStripSecondEnd;
			_maximumRaise = def.maximumRaise;
		}

		public override void Tick(float delta)
		{
			if (_unit == null || _base == null || _stripEnd1 == null || _stripEnd2 == null)
				return;
			float? contactHeight;
			float  voltage;
			if (GetWireHeightAndVoltage != null)
				(contactHeight, voltage) = GetWireHeightAndVoltage(_unit.transform, _base, _stripEnd1, _stripEnd2, 0.0f);
			else
			{
				contactHeight = null;
				voltage       = 0.0f;
			}
			if (contactHeight == null)
			{
				_voltageReadOut.Value = _voltageNormalizedReadOut.Value = 0.0f;
				_heightReadOut.Value = _maximumRaise;
			}
			else
			{
				_voltageReadOut.Value = voltage;
				_voltageNormalizedReadOut.Value = voltage / 1500.0f;
				_heightReadOut.Value = (float) contactHeight;
			}
		}
	}
}
