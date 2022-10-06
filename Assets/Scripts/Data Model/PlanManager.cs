﻿using System.Collections.Generic;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;
using UnityEngine;
using System;

namespace MSP2050.Scripts
{
	public class PlanManager : MonoBehaviour
	{
		public enum PlanViewState { All, Base, Changes, Time };

		private static PlanManager singleton;
		public static PlanManager Instance
		{
			get
			{
				if (singleton == null)
					singleton = FindObjectOfType<PlanManager>();
				return singleton;
			}
		}

		private List<Plan> plans = new List<Plan>();
		private Dictionary<int, PlanLayer> planLayers = new Dictionary<int, PlanLayer>();
		private HashSet<Plan> unseenPlanChanges = new HashSet<Plan>();

		public delegate void PlansEventDelegate(Plan plan);
		public delegate void PlansUpdateEventDelegate(Plan plan, int oldTime);
		public event PlansEventDelegate OnPlanVisibleInUIEvent;
		public event PlansUpdateEventDelegate OnPlanUpdateInUIEvent;
		public event PlansUpdateEventDelegate OnPlanHideInUIEvent;

		//Viewing & Viewstates
		[HideInInspector] public PlanViewState planViewState = PlanViewState.All;
		[HideInInspector] public Plan planViewing;
		[HideInInspector] public int timeViewing = -1; //Used if planViewing is null. -1 is current time.
		[HideInInspector] public bool inPlanUIChange;
		private int planToViewOnUpdate;

		private bool ignoreRedrawOnViewStateChange = false;

		public List<Plan> Plans => plans;

		void Start()
		{
			if (singleton != null && singleton != this)
				Destroy(this);
			else
				singleton = this;
		}

		void OnDestroy()
		{
			singleton = null;
		}

		public Plan ProcessReceivedPlan(PlanObject planObject, Dictionary<AbstractLayer, int> layerUpdateTimes)
		{
			int planID = planObject.id;
			Plan targetPlan = GetPlanWithID(planID);

			if (targetPlan != null)
			{
				targetPlan.UpdatePlan(planObject, layerUpdateTimes);
			}
			else
			{
				targetPlan = new Plan(planObject, layerUpdateTimes);
				AddPlan(targetPlan);
				PlanAdded(targetPlan);
			}

			//RestrictionAreaManager.instance.ProcessReceivedRestrictions(targetPlan, planObject.restriction_settings);
			return targetPlan;
		}

		public void AddPlan(Plan newPlan)
		{
			if (plans.Count == 0)
			{
				plans.Add(newPlan);
				return;
			}

			for (int i = 0; i < plans.Count; i++)
				if (plans[i].StartTime > newPlan.StartTime)
				{
					plans.Insert(i, newPlan);
					return;
				}

			plans.Add(newPlan);
		}

		public void UpdatePlanTime(Plan updatedPlan)
		{
			plans.Remove(updatedPlan);
			AddPlan(updatedPlan);
		}

		public void SetPlanViewState(PlanViewState state, bool redraw = true)
		{
			bool needsRedraw = redraw && (!ignoreRedrawOnViewStateChange && planViewState != state);
			planViewState = state;
			if (needsRedraw)
				LayerManager.Instance.RedrawVisibleLayers();
		}

		public void ShowWorldAt(int time)
		{
			if (timeViewing == time || planViewing != null)
				return;
			if (time == -1)
				LayerManager.Instance.UpdateVisibleLayersToBase();
			else
				LayerManager.Instance.UpdateVisibleLayersToTime(time);
			timeViewing = time;
		}

		public void ShowPlan(Plan plan)
		{
			if (Main.InEditMode)
				return;

			//InterfaceCanvas.Instance.viewTimeWindow.CloseWindow(false);
			InterfaceCanvas.Instance.ignoreLayerToggleCallback = true;
			planViewing = plan;
			timeViewing = -1;
			InterfaceCanvas.Instance.timeBar.SetViewMode(TimeBar.WorldViewMode.Plan, false);//Needs to be done before redraw
			LayerManager.Instance.UpdateVisibleLayersToPlan(plan);
			InterfaceCanvas.Instance.ignoreLayerToggleCallback = false;
			InterfaceCanvas.Instance.activePlanWindow.SetToPlan(plan);
		}

		public void HideCurrentPlan(bool updateLayers = true)
		{
			if (Main.InEditMode)
				return;

			InterfaceCanvas.Instance.ignoreLayerToggleCallback = true;
			planViewing = null;

			//Doesnt have to redraw as we'll do so when updating layers to base anyway
			ignoreRedrawOnViewStateChange = true;
			InterfaceCanvas.Instance.activePlanWindow.SetViewMode(PlanManager.PlanViewState.All);
			ignoreRedrawOnViewStateChange = false;

			if(updateLayers)
				LayerManager.Instance.UpdateVisibleLayersToBase();
			InterfaceCanvas.Instance.ignoreLayerToggleCallback = false;
			InterfaceCanvas.Instance.activePlanWindow.CloseWindow();
			InterfaceCanvas.Instance.timeBar.SetViewMode(TimeBar.WorldViewMode.Normal, false);
		}

		public SubEntityPlanState GetSubEntityPlanState(SubEntity subEntity)
		{
			//added, moved, removed, notinplan, notshown
			PlanLayer currentPlanLayer = subEntity.Entity.Layer.CurrentPlanLayer();
			bool layerInPlan = planViewing == null || planViewing.IsLayerpartOfPlan(subEntity.Entity.Layer);

			if (planViewState == PlanViewState.All)
			{
				if (currentPlanLayer == null) //Only show the base layer
				{
					if (subEntity.Entity.Layer.IsIDInActiveGeometry(subEntity.GetDatabaseID()))
						return SubEntityPlanState.NotInPlan;
					else
						return SubEntityPlanState.NotShown;
				}
				if (!layerInPlan)
				{
					if (subEntity.Entity.Layer.IsIDInActiveGeometry(subEntity.GetDatabaseID()))
						return SubEntityPlanState.NotInPlan;
					else
						return SubEntityPlanState.NotShown;
				}
				if (currentPlanLayer.IsDatabaseIDInNewGeometry(subEntity.GetDatabaseID()))
				{
					if (!currentPlanLayer.BaseLayer.IsEntityTypeVisible(subEntity.Entity.EntityTypes))
						return SubEntityPlanState.NotShown;
					if (subEntity.Entity.Layer.IsPersisIDCurrentlyNew(subEntity.GetPersistentID()))
						return SubEntityPlanState.Added;
					return SubEntityPlanState.Moved;
				}
				if (subEntity.Entity.Layer.IsIDInActiveGeometry(subEntity.GetDatabaseID()))
				{
					if (currentPlanLayer.IsPersistentIDInRemovedGeometry(subEntity.GetPersistentID()))
						return SubEntityPlanState.Removed;
					return SubEntityPlanState.NotInPlan;
				}
			}
			else if (planViewState == PlanViewState.Base)
			{
				if (currentPlanLayer == null) //Only show the base layer
				{
					if (subEntity.Entity.Layer.IsIDInActiveGeometry(subEntity.GetDatabaseID()))
						return SubEntityPlanState.NotInPlan;
					else
						return SubEntityPlanState.NotShown;
				}
				if (!layerInPlan)
				{
					if (subEntity.Entity.Layer.IsIDInActiveGeometry(subEntity.GetDatabaseID()))
						return SubEntityPlanState.NotInPlan;
					else
						return SubEntityPlanState.NotShown;
				}
				if (currentPlanLayer.IsPersistentIDInRemovedGeometry(subEntity.GetPersistentID()) || subEntity.Entity.Layer.IsDatabaseIDPreModified(subEntity.GetDatabaseID()))
					return SubEntityPlanState.NotInPlan;
				if (subEntity.Entity.Layer.IsPersisIDCurrentlyNew(subEntity.GetPersistentID()) || currentPlanLayer.IsDatabaseIDInNewGeometry(subEntity.GetDatabaseID()))
					return SubEntityPlanState.NotShown;
				if (subEntity.Entity.Layer.IsIDInActiveGeometry(subEntity.GetDatabaseID()))
					return SubEntityPlanState.NotInPlan;
			}
			else if (planViewState == PlanViewState.Changes)
			{
				if (currentPlanLayer == null) //Only show the base layer
					return SubEntityPlanState.NotShown;
				if (!layerInPlan)
					return SubEntityPlanState.NotShown;
				if (currentPlanLayer.IsDatabaseIDInNewGeometry(subEntity.GetDatabaseID()))
				{
					if (!currentPlanLayer.BaseLayer.IsEntityTypeVisible(subEntity.Entity.EntityTypes))
						return SubEntityPlanState.NotShown;
					if (subEntity.Entity.Layer.IsPersisIDCurrentlyNew(subEntity.GetPersistentID()))
						return SubEntityPlanState.Added;
					return SubEntityPlanState.Moved;
				}
				if (subEntity.Entity.Layer.IsIDInActiveGeometry(subEntity.GetDatabaseID()) && currentPlanLayer.IsPersistentIDInRemovedGeometry(subEntity.GetPersistentID()))
					return SubEntityPlanState.Removed;
			}
			else //PlanViewState.Time
			{
				if (subEntity.Entity.Layer.IsIDInActiveGeometry(subEntity.GetDatabaseID()))
					return SubEntityPlanState.NotInPlan;
				else
					return SubEntityPlanState.NotShown;
			}
			return SubEntityPlanState.NotShown;
		}

		public void ViewPlanOnMap(Plan plan)
		{
			foreach (PlanLayer planLayer in plan.PlanLayers)
				LayerManager.Instance.ShowLayer(planLayer.BaseLayer);

			CameraManager.Instance.ZoomToBounds(plan.GetBounds());
		}

		public void ViewPlanLayerOnMap(PlanLayer planLayer)
		{
			LayerManager.Instance.ShowLayer(planLayer.BaseLayer);
			CameraManager.Instance.ZoomToBounds(planLayer.GetBounds());
		}

		public int GetPlanCount()
		{
			return plans.Count;
		}

		public Plan GetPlanAtIndex(int index)
		{
			return plans[index];
		}

		public Plan GetPlanWithID(int planID)
		{
			foreach (Plan plan in plans)
			{
				if (plan.ID == planID)
				{
					return plan;
				}
			}
			return null;
		}

		public List<Plan> GetAllPlansFrom(int month)
		{
			List<Plan> result = new List<Plan>();
			for (int i = plans.Count - 1; i >= 0; i--)
			{
				if (plans[i].StartTime < month)
					break;
				result.Add(plans[i]);
			}
			return result;
		}

		/// <summary>
		/// Returns plan layers for a base layer from a specific month onwards
		/// </summary>
		/// <param name="baseLayer">The base layer we need to get the geometry from</param>
		/// <param name="planStartTime">Exclusive from what date on we want to get the layers</param>
		/// <param name="onlyInfluencingPlans">Only plans that are in the influencing state</param>
		/// <returns></returns>
		public List<PlanLayer> GetPlanLayersForBaseLayerFrom(AbstractLayer baseLayer, int planStartTime, bool onlyInfluencingPlans)
		{
			List<PlanLayer> result = new List<PlanLayer>(32);
			//Iterate forwards so the list is in order from first occuring layer to last occuring layer. This helps us with checks in the future
			for (int i = 0; i < plans.Count; ++i)
			{
				Plan plan = plans[i];
				if (plan.StartTime <= planStartTime ||
				    (onlyInfluencingPlans && !plan.InInfluencingState))
				{
					continue;
				}

				PlanLayer planLayer = plan.GetPlanLayerForLayer(baseLayer);
				if (planLayer != null)
				{
					result.Add(planLayer);
				}
			}
			return result;
		}

		public Plan FindFirstPlanChangingGeometry(int fromMonth, int entityPersistentId, AbstractLayer baseLayer)
		{
			Plan result = null;
			for (int i = 0; i < plans.Count; ++i)
			{
				Plan plan = plans[i];
				if (plan.StartTime <= fromMonth)
				{
					continue;
				}

				PlanLayer planLayer = plan.GetPlanLayerForLayer(baseLayer);
				if (planLayer != null)
				{
					if (planLayer.IsPersistentIDInNewGeometry(entityPersistentId) ||
					    planLayer.IsPersistentIDInRemovedGeometry(entityPersistentId))
					{
						result = plan;
						break;
					}
				}
			}
			return result;
		}

		public PlanLayer GetPlanLayer(int ID)
		{
			if (planLayers.ContainsKey(ID))
				return planLayers[ID];
			else
				return null;
		}

		public bool RemovePlanLayer(PlanLayer planLayer)
		{
			return planLayers.Remove(planLayer.ID);
		}

		/// <summary>
		/// Called whenever a new month starts
		/// </summary>
		/// <param name="newMonth">month that just started</param>
		public void MonthTick(int newMonth)
		{
			//Advance time on layers (merging approved ones) 
			foreach (AbstractLayer layer in LayerManager.Instance.GetAllValidLayers())
				layer.AdvanceTimeTo(newMonth);
		}


		/////////////////////////////////////////
		// EVENT HANDLERS, MOSTLY FOR UI STUFF //
		/////////////////////////////////////////

		private void PlanAdded(Plan plan)
		{
			//Add planLayers to manager, but don't add to UI individually (done in a batch by plan)
			foreach (PlanLayer planLayer in plan.PlanLayers)
				PlanLayerAdded(plan, planLayer, false);

			//Show plan if it isnt a hidden plan
			if (plan.StartTime >= 0 || SessionManager.Instance.AreWeGameMaster)
			{
				PlansMonitor.AddPlan(plan);
				if (plan.ShouldBeVisibleInTimeline)
				{
					OnPlanVisibleInUIEvent(plan);
				}
			}
		}

		public void UpdatePlanInUI(Plan plan, bool nameOrDescriptionChanged, bool timeChanged, bool stateChanged, bool layersChanged, bool typeChanged, bool forceMonitorUpdate, int oldTime, Plan.PlanState oldState, bool inTimelineBefore)
		{
			bool timeLineUpdated = false;
			bool inTimelineNow = plan.ShouldBeVisibleInTimeline;

			if (nameOrDescriptionChanged)
			{
				PlanDetails.UpdateNameAndDescription(plan);
				if (planViewing == plan && !Main.InEditMode)
					InterfaceCanvas.Instance.activePlanWindow.UpdateNameAndDate();			
			}
			if (stateChanged)
			{
				//Didn't see icon before, should see now
				if (!inTimelineBefore && inTimelineNow)
				{
					OnPlanVisibleInUIEvent(plan);
					timeLineUpdated = true;
				}
				//Saw plan before, shouldn't see now
				else if (inTimelineBefore && !inTimelineNow)
				{
					OnPlanHideInUIEvent(plan, oldTime);
					timeLineUpdated = true;
				}
			}

			//Update edit button availability in active plan window
			if((stateChanged || layersChanged) && planViewing == plan)
				InterfaceCanvas.Instance.activePlanWindow.UpdateEditButtonActivity();

			if (timeChanged)
			{
				//Plan didnt change influencing state and should be visible to this client: update
				if (!timeLineUpdated && inTimelineNow)
					OnPlanUpdateInUIEvent(plan, oldTime);
				PlanDetails.ChangeDate(plan);
				if (planViewing == plan && !Main.InEditMode)
				{
					InterfaceCanvas.Instance.activePlanWindow.UpdateNameAndDate();							
					InterfaceCanvas.Instance.timeBar.UpdatePlanViewing();
					LayerManager.Instance.UpdateVisibleLayersToPlan(plan);
				}
			}
			if (stateChanged || timeChanged || nameOrDescriptionChanged || forceMonitorUpdate)
			{
				PlansMonitor.UpdatePlan(plan, nameOrDescriptionChanged, timeChanged, stateChanged);
			}

			//These changes don't require a general update, only plandetails if they are being viewed
			if (plan == PlanDetails.GetSelectedPlan())
			{
				if (!plan.ShouldBeVisibleInUI)
					PlanDetails.SelectPlan(null);
				else
				{
					if (stateChanged)
						PlanDetails.UpdateStatus();
					if (typeChanged)
						PlanDetails.UpdateTabAvailability();
				}
				PlanDetails.UpdateTabContent();
			}
		}

		public void PlanLockUpdated(Plan plan)
		{
			PlansMonitor.SetLockIcon(plan, plan.IsLocked);
			if(Main.InEditMode && Main.CurrentlyEditingPlan == plan)
			{
				PlanDetails.instance.CancelEditingContent();
				DialogBoxManager.instance.NotificationWindow("Plan Unexpectedly Unlocked", "Plan has been unlocked by an external party. All changes have been discarded.", null);
			}
		}

		public bool UserHasPlanLocked(int sessionID)
		{
			foreach (Plan plan in plans)
				if (plan.LockedBy == sessionID)
					return true;
			return false;
		}

		public void PlanLayerAdded(Plan plan, PlanLayer addedLayer, bool addToUI = true)
		{
			planLayers[addedLayer.ID] = addedLayer;
			IssueManager.Instance.InitialiseIssuesForPlanLayer(addedLayer);
			if (addToUI)
			{ 
				//TODO: if viewing plan, add to active plan window?
			}
		}

		public void PlanLayerRemoved(Plan plan, PlanLayer removedLayer)
		{
			IssueManager.Instance.DeleteIssuesForPlanLayer(removedLayer);
			RemovePlanLayer(removedLayer);
		}

		public void ViewPlanWithIDWhenReceived(int targetPlanID)
		{
			bool found = false;
			foreach (Plan plan in plans)
			{
				if (plan.ID == targetPlanID)
				{
					found = true;
					ShowPlan(plan);
					PlanDetails.SelectPlan(plan);
				}
			}

			if(!found)
				planToViewOnUpdate = targetPlanID;
		}

		public void CheckIfExpectedPlanReceived()
		{
			if (planToViewOnUpdate == -1)
				return;

			foreach (Plan plan in plans)
			{
				if (plan.ID == planToViewOnUpdate)
				{
					planToViewOnUpdate = -1;
					ShowPlan(plan);
					PlanDetails.SelectPlan(plan);
				}
			}
		}
	}
}