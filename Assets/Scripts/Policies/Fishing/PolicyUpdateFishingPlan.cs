﻿using System.Collections.Generic;
using UnityEngine;

namespace MSP2050.Scripts
{
	public class PolicyUpdateFishingPlan : APolicyData
	{
		public List<FishingObject> fishing;
	}

	public class FishingObject
	{
		public int country_id;
		public string type;
		public float amount;
	}
}