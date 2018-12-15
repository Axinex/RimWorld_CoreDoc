using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse.AI;

namespace Verse {

	public enum DestroyMode:byte {
		Vanish,
		WillReplace,    // This is basically Vanish, but with the expectation that it will be replaced immediately with something roughly equivalent. Nothing irreversible will be done to the game state (falling roofs, fog of war, etc.)
		KillFinalize,
		Deconstruct,
		FailConstruction,
		Cancel,
		Refund,
	}

	public class Thing:Entity, IExposable, ISelectable, ILoadReferenceable, ISignalReceiver {
		//Vars
		public ThingDef def = null;
		public int thingIDNumber = -1;  // Note: Generated from a random source that does not include the map seed number; don't use in calculations that might be mapgen-sensitive!
		private sbyte mapIndexOrState = UnspawnedState; // if >= 0 then map index, otherwise state
		private IntVec3 positionInt = IntVec3.Invalid;
		private Rot4 rotationInt = Rot4.North;
		public int stackCount = 1;
		protected Faction factionInt = null;
		private ThingDef stuffInt = null;
		private Graphic graphicInt = null;
		private int hitPointsInt = -1;
		public ThingOwner holdingOwner = null;
		public List<string> questTags = null;

		//Constants
		protected const sbyte UnspawnedState = -1;
		private const sbyte MemoryState = -2; // destroyed
		private const sbyte DiscardedState = -3; // destroyed, means the thing is no longer managed by anything nor saved

		//==============================================================================	
		//================================= Properties =================================
		//==============================================================================
		public virtual int HitPoints { get => this.hitPointsInt; set => this.hitPointsInt=value; }
		public int MaxHitPoints => Mathf.RoundToInt(this.GetStatValue(StatDefOf.MaxHitPoints));
		public float MarketValue => this.GetStatValue(StatDefOf.MarketValue);
		public bool FlammableNow {
			get {
				if(this.GetStatValue(StatDefOf.Flammability)<0.01f)
					return false;

				// If there's a fire bulwark in this square, then we're protected from fire
				// Bulwarks are never protected by other bulwarks
				if(this.Spawned&&!this.FireBulwark) {
					List<Thing> thingList = this.Position.GetThingList(this.Map);
					if(thingList!=null) {
						for(int i = 0; i<thingList.Count; i++) {
							if(thingList[i].FireBulwark)
								return false;
						}
					}
				}

				return true;
			}
		}
		// Whether this protects other objects, in this square, from fire
		// This is so conduits aren't flammable when buried in walls
		public virtual bool FireBulwark => this.def.Fillage==FillCategory.Full;
		public bool Destroyed => this.mapIndexOrState==MemoryState||this.mapIndexOrState==DiscardedState;
		public bool Discarded => this.mapIndexOrState==DiscardedState;
		public bool Spawned {
			get {
				// I am slightly worried about performance here but I'm currently more worried about correctness here

				if(this.mapIndexOrState<0)
					return false;   // discarded, destroyed, unspawned

				if(this.mapIndexOrState<Find.Maps.Count)
					return true;

				Log.ErrorOnce("Thing is associated with invalid map index", 64664487);
				return false;
			}
		}
		public bool SpawnedOrAnyParentSpawned => this.SpawnedParentOrMe!=null;
		public Thing SpawnedParentOrMe {
			get {
				if(this.Spawned)
					return this;

				if(this.ParentHolder!=null)
					return ThingOwnerUtility.SpawnedParentOrMe(this.ParentHolder);
				else
					return null;
			}
		}
		public Map Map {
			get {
				if(this.mapIndexOrState>=0)
					return Find.Maps[this.mapIndexOrState];
				else
					return null;
			}
		}
		public Map MapHeld {
			get {
				//Optimization for spawned things
				if(this.Spawned)
					return this.Map;

				if(this.ParentHolder!=null)
					return ThingOwnerUtility.GetRootMap(this.ParentHolder);
				else
					return null;
			}
		}
		public IntVec3 Position {
			get => this.positionInt;
			set {
				if(value==this.positionInt)
					return;

				if(this.Spawned) {
					if(this.def.AffectsRegions)
						Log.Warning("Changed position of a spawned thing which affects regions. This is not supported.");

					this.DirtyMapMesh(this.Map);
					RegionListersUpdater.DeregisterInRegions(this, this.Map);
					this.Map.thingGrid.Deregister(this);
				}

				this.positionInt=value;

				if(this.Spawned) {
					this.Map.thingGrid.Register(this);
					RegionListersUpdater.RegisterInRegions(this, this.Map);
					this.DirtyMapMesh(this.Map);

					if(this.def.AffectsReachability)
						this.Map.reachability.ClearCache();
				}
			}
		}
		public IntVec3 PositionHeld {
			get {
				if(this.Spawned)
					return this.Position;

				IntVec3 position = ThingOwnerUtility.GetRootPosition(this.ParentHolder);
				if(position.IsValid)
					return position;

				//Just return the last position we were spawned at
				return this.Position;
			}
		}
		public Rot4 Rotation {
			get => this.rotationInt;
			set {
				if(value==this.rotationInt)
					return;

				if(this.Spawned&&(this.def.size.x!=1||this.def.size.z!=1)) {
					if(this.def.AffectsRegions)
						Log.Warning("Changed rotation of a spawned non-single-cell thing which affects regions. This is not supported.");

					RegionListersUpdater.DeregisterInRegions(this, this.Map);
					this.Map.thingGrid.Deregister(this);
				}

				this.rotationInt=value;

				if(this.Spawned&&(this.def.size.x!=1||this.def.size.z!=1)) {
					this.Map.thingGrid.Register(this);
					RegionListersUpdater.RegisterInRegions(this, this.Map);

					if(this.def.AffectsReachability)
						this.Map.reachability.ClearCache();
				}
			}
		}
		public bool Smeltable =>
				//Must be marked smeltable
				//If MadeFromStuff, stuff must be smeltable
				this.def.smeltable&&(!this.def.MadeFromStuff||this.Stuff.stuffProps.smeltable);
		public IThingHolder ParentHolder => this.holdingOwner!=null ? this.holdingOwner.Owner : null;
		public Faction Faction => this.factionInt;
		public string ThingID {
			get {
				if(this.def.HasThingIDNumber)
					return this.def.defName+this.thingIDNumber.ToString();
				else
					return this.def.defName;
			}
			set =>
					//Pull numbers off the end of the string and plug them into the ID number	
					this.thingIDNumber=IDNumberFromThingID(value);
		}
		public static int IDNumberFromThingID(string thingID) {
			//Safety code for catching a rare overflow exception
			string numString = Regex.Match(thingID, @"\d+$").Value; //Will break on def names like "M-16"
			int idNum = 0;
			try {
				idNum=System.Convert.ToInt32(numString);
			}
			catch(System.Exception e) {
				Log.Error("Could not convert id number from thingID="+thingID+", numString="+numString+" Exception="+e.ToString());
			}
			return idNum;
		}
		public IntVec2 RotatedSize {
			get {
				if(!this.rotationInt.IsHorizontal)
					return this.def.size;
				else
					return new IntVec2(this.def.size.z, this.def.size.x);
			}
		}
		/// <summary>Uncapitalized label with stack count.</summary>
		public override string Label {
			get {
				if(this.stackCount>1)
					return this.LabelNoCount+" x"+GenString.ToStringCached(this.stackCount);

				return this.LabelNoCount;
			}
		}
		/// <summary>Uncapitalized label without stack count.</summary>
		public virtual string LabelNoCount => GenLabel.ThingLabel(this, 1);
		/// <summary>Capitalized label with stack count.</summary>
		public override string LabelCap => this.Label.CapitalizeFirst();
		public virtual string LabelCapNoCount => this.LabelNoCount.CapitalizeFirst();
		/// <summary>Shortest possible version of uncapitalized label without stack count.</summary>
		public override string LabelShort => this.LabelNoCount;

		public virtual bool IngestibleNow {
			get {
				if(this.IsBurning())
					return false;

				return this.def.IsIngestible;
			}
		}
		public ThingDef Stuff => this.stuffInt;
		public Graphic DefaultGraphic {
			get {
				if(this.graphicInt==null) {
					if(this.def.graphicData==null)
						return BaseContent.BadGraphic;

					this.graphicInt=this.def.graphicData.GraphicColoredFor(this);
				}

				return this.graphicInt;
			}
		}
		public virtual Graphic Graphic => this.DefaultGraphic;
		public virtual IntVec3 InteractionCell => ThingUtility.InteractionCellWhenAt(this.def, this.Position, this.Rotation, this.Map);
		public float AmbientTemperature {
			get {
				//Optimization for spawned things
				if(this.Spawned)
					return GenTemperature.GetTemperatureForCell(this.Position, this.Map);

				if(this.ParentHolder!=null) {
					IThingHolder cur = this.ParentHolder;

					while(cur!=null) {
						if(ThingOwnerUtility.TryGetFixedTemperature(cur, this, out float temp))
							return temp;

						cur=cur.ParentHolder;
					}
				}

				if(this.SpawnedOrAnyParentSpawned)
					return GenTemperature.GetTemperatureForCell(this.PositionHeld, this.MapHeld);

				if(this.Tile>=0)
					return GenTemperature.GetTemperatureAtTile(this.Tile);

				return TemperatureTuning.DefaultTemperature;
			}
		}
		public int Tile {
			get {
				//Optimization for spawned things
				if(this.Spawned)
					return this.Map.Tile;

				if(this.ParentHolder!=null)
					return ThingOwnerUtility.GetRootTile(this.ParentHolder);
				else
					return RimWorld.Planet.Tile.Invalid;
			}
		}
		public bool Suspended {
			get {
				//Optimization for spawned things
				if(this.Spawned)
					return false;

				if(this.ParentHolder!=null)
					return ThingOwnerUtility.ContentsSuspended(this.ParentHolder);
				else
					return false;
			}
		}
		public virtual string DescriptionDetailed => this.def.DescriptionDetailed;
		public virtual string DescriptionFlavor => this.def.description;

		//=======================================================================================	
		//============================== Creation and destruction ===============================
		//=======================================================================================

		public Thing() : base() { }

		public virtual void PostMake() {
			ThingIDMaker.GiveIDTo(this);

			if(this.def.useHitPoints)
				this.HitPoints=Mathf.RoundToInt(this.MaxHitPoints*Mathf.Clamp01(this.def.startingHpRange.RandomInRange));
		}

		public string GetUniqueLoadID() => "Thing_"+this.ThingID;

		public override void SpawnSetup(Map map, bool respawningAfterLoad) {
			if(this.Destroyed) {
				Log.Error("Spawning destroyed thing "+this+" at "+this.Position+". Correcting.");
				this.mapIndexOrState=UnspawnedState;
				if(this.HitPoints<=0&&this.def.useHitPoints)
					this.HitPoints=1;
			}

			if(this.Spawned) {
				Log.Error("Tried to spawn already-spawned thing "+this+" at "+this.Position);
				return;
			}

			int mapIndex = Find.Maps.IndexOf(map);

			if(mapIndex<0) {
				Log.Error("Tried to spawn thing "+this+", but the map provided does not exist.");
				return;
			}

			if(this.stackCount>this.def.stackLimit) {
				Log.Error("Spawned "+this+" with stackCount "+this.stackCount+" but stackLimit is "+this.def.stackLimit+". Truncating.");
				this.stackCount=this.def.stackLimit;
			}

			this.mapIndexOrState=(sbyte)mapIndex;

			//Register in regions
			RegionListersUpdater.RegisterInRegions(this, map);

			//Register in grids
			if(!map.spawnedThings.TryAdd(this, false))
				Log.Error("Couldn't add thing "+this+" to spawned things.");

			map.listerThings.Add(this);
			map.thingGrid.Register(this);

			if(Find.TickManager!=null)
				Find.TickManager.RegisterAllTickabilityFor(this);

			this.DirtyMapMesh(map);

			if(this.def.drawerType!=DrawerType.MapMeshOnly)
				map.dynamicDrawManager.RegisterDrawable(this);

			map.tooltipGiverList.Notify_ThingSpawned(this);

			if(this.def.graphicData!=null&&this.def.graphicData.Linked) {
				map.linkGrid.Notify_LinkerCreatedOrDestroyed(this);
				map.mapDrawer.MapMeshDirty(this.Position, MapMeshFlag.Things, true, false);//Linkers force adj square rebuild
			}

			if(!this.def.CanOverlapZones)
				map.zoneManager.Notify_NoZoneOverlapThingSpawned(this);

			if(this.def.AffectsRegions)
				map.regionDirtyer.Notify_ThingAffectingRegionsSpawned(this);

			if(this.def.pathCost!=0||this.def.passability==Traversability.Impassable)
				map.pathGrid.RecalculatePerceivedPathCostUnderThing(this);

			if(this.def.AffectsReachability)
				map.reachability.ClearCache();

			map.coverGrid.Register(this);

			if(this.def.category==ThingCategory.Item) {
				map.listerHaulables.Notify_Spawned(this);
				map.listerMergeables.Notify_Spawned(this);
			}

			map.attackTargetsCache.Notify_ThingSpawned(this);

			Region region = map.regionGrid.GetValidRegionAt_NoRebuild(this.Position);
			Room room = region==null ? null : region.Room;

			if(room!=null)
				room.Notify_ContainedThingSpawnedOrDespawned(this);

			StealAIDebugDrawer.Notify_ThingChanged(this);

			IHaulDestination haulDestination = this as IHaulDestination;
			if(haulDestination!=null)
				map.haulDestinationManager.AddHaulDestination(haulDestination);

			if(this is IThingHolder&&Find.ColonistBar!=null)
				Find.ColonistBar.MarkColonistsDirty();

			if(this.def.category==ThingCategory.Item) {
				SlotGroup group = this.Position.GetSlotGroup(map);
				if(group!=null&&group.parent!=null)
					group.parent.Notify_ReceivedThing(this);
			}

			if(this.def.receivesSignals)
				Find.SignalManager.RegisterReceiver(this);

			if(!respawningAfterLoad)
				QuestUtility.SendQuestTargetSignals(this.questTags, QuestUtility.QuestTargetSignalPart_Spawned, this.Named(SignalArgsNames.Subject));
		}

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish) {
			if(this.Destroyed) {
				Log.Error("Tried to despawn "+this.ToStringSafe()+" which is already destroyed.");
				return;
			}

			if(!this.Spawned) {
				Log.Error("Tried to despawn "+this.ToStringSafe()+" which is not spawned.");
				return;
			}

			ProfilerThreadCheck.BeginSample("DeSpawn");

			Map map = this.Map; // before changing state to UnspawnedState!

			//Remove from regions
			RegionListersUpdater.DeregisterInRegions(this, map);

			//Deregister from grids
			map.spawnedThings.Remove(this);
			map.listerThings.Remove(this);
			map.thingGrid.Deregister(this);
			map.coverGrid.DeRegister(this);

			if(this.def.receivesSignals)
				Find.SignalManager.DeregisterReceiver(this);

			map.tooltipGiverList.Notify_ThingDespawned(this);

			if(this.def.graphicData!=null&&this.def.graphicData.Linked) {
				map.linkGrid.Notify_LinkerCreatedOrDestroyed(this);
				map.mapDrawer.MapMeshDirty(this.Position, MapMeshFlag.Things, true, false);//Linkers force adj square rebuild
			}

			Find.Selector.Deselect(this);

			this.DirtyMapMesh(map);

			if(this.def.drawerType!=DrawerType.MapMeshOnly)
				map.dynamicDrawManager.DeRegisterDrawable(this);

			Region region = map.regionGrid.GetValidRegionAt_NoRebuild(this.Position);
			Room room = region==null ? null : region.Room;

			if(room!=null)
				room.Notify_ContainedThingSpawnedOrDespawned(this);

			if(this.def.AffectsRegions)
				map.regionDirtyer.Notify_ThingAffectingRegionsDespawned(this);

			if(this.def.pathCost!=0||this.def.passability==Traversability.Impassable)
				map.pathGrid.RecalculatePerceivedPathCostUnderThing(this);

			if(this.def.AffectsReachability)
				map.reachability.ClearCache();

			Find.TickManager.DeRegisterAllTickabilityFor(this);

			this.mapIndexOrState=UnspawnedState;

			if(this.def.category==ThingCategory.Item) {
				map.listerHaulables.Notify_DeSpawned(this);
				map.listerMergeables.Notify_DeSpawned(this);
			}

			map.attackTargetsCache.Notify_ThingDespawned(this);

			//Remove physical reservations (you can't be interacting with something that has been despawned)
			map.physicalInteractionReservationManager.ReleaseAllForTarget(this);

			StealAIDebugDrawer.Notify_ThingChanged(this);

			IHaulDestination haulDestination = this as IHaulDestination;
			if(haulDestination!=null)
				map.haulDestinationManager.RemoveHaulDestination(haulDestination);

			if(this is IThingHolder&&Find.ColonistBar!=null)
				Find.ColonistBar.MarkColonistsDirty();

			if(this.def.category==ThingCategory.Item) {
				SlotGroup group = this.Position.GetSlotGroup(map);
				if(group!=null&&group.parent!=null)
					group.parent.Notify_LostThing(this);
			}

			QuestUtility.SendQuestTargetSignals(this.questTags, QuestUtility.QuestTargetSignalPart_Despawned, this.Named(SignalArgsNames.Subject));

			ProfilerThreadCheck.EndSample();
		}

		public virtual void Kill(DamageInfo? dinfo = null, Hediff exactCulprit = null) => Destroy(DestroyMode.KillFinalize);

		public static bool allowDestroyNonDestroyable = false;
		public virtual void Destroy(DestroyMode mode = DestroyMode.Vanish) {
			if(!allowDestroyNonDestroyable) {
				if(!this.def.destroyable) {
					Log.Error("Tried to destroy non-destroyable thing "+this);
					return;
				}
			}

			if(this.Destroyed) {
				Log.Error("Tried to destroy already-destroyed thing "+this);
				return;
			}

			bool wasSpawned = this.Spawned;
			Map map = this.Map; // before DeSpawn!

			if(this.Spawned)
				this.DeSpawn(mode);

			this.mapIndexOrState=MemoryState;

			if(this.def.DiscardOnDestroyed)
				this.Discard();

			CompExplosive explosive = this.TryGetComp<CompExplosive>();
			bool destroyedThroughDetonation = explosive!=null&&explosive.destroyedThroughDetonation;
			if(wasSpawned&&!destroyedThroughDetonation)
				GenLeaving.DoLeavingsFor(this, map, mode);

			if(this.holdingOwner!=null)
				this.holdingOwner.Notify_ContainedItemDestroyed(this);

			this.RemoveAllReservationsAndDesignationsOnThis();

			//Only pawns are allowed to exist when destroyed,
			//all other stacks are set to 0
			if(!(this is Pawn))
				this.stackCount=0;

			QuestUtility.SendQuestTargetSignals(this.questTags, QuestUtility.QuestTargetSignalPart_Destroyed, this.Named(SignalArgsNames.Subject));
		}

		public virtual void PreTraded(TradeAction action, Pawn playerNegotiator, ITrader trader) {
		}

		public virtual void PostGeneratedForTrader(TraderKindDef trader, int forTile, Faction forFaction) {
			if(this.def.colorGeneratorInTraderStock!=null)
				this.SetColor(this.def.colorGeneratorInTraderStock.NewRandomizedColor(), reportFailure: true);
		}

		/// <summary>
		/// Called when a map is removed and this thing is still spawned.
		/// We don't call DeSpawn() on every thing for performance reasons,
		/// and because we don't really care about the map anymore anyway.
		/// However, if the thing affects something beyond the map then it should be cleaned up here.
		/// </summary>
		public virtual void Notify_MyMapRemoved() {
			if(this.def.receivesSignals)
				Find.SignalManager.DeregisterReceiver(this);

			//We set the state to discarded just in case something still references this thing, like TargetInfo

			//We don't discard pawns (or their inventory) because even though they have a Map parent they can be world pawns (corpses)
			if(!ThingOwnerUtility.AnyParentIs<Pawn>(this))
				this.mapIndexOrState=DiscardedState;

			//Remove all designations, normally it happens in Destroy() but we don't destroy things whose maps are destroyed
			this.RemoveAllReservationsAndDesignationsOnThis();
		}

		public void ForceSetStateToUnspawned() => this.mapIndexOrState=UnspawnedState;

		public void DecrementMapIndex() {
			if(this.mapIndexOrState<=0) // 0 too because -1 is not a map index
			{
				Log.Warning("Tried to decrement map index for "+this+", but mapIndexOrState="+this.mapIndexOrState);
				return;
			}

			this.mapIndexOrState--;
		}

		private void RemoveAllReservationsAndDesignationsOnThis() {
			//Optimization
			if(this.def.category==ThingCategory.Mote)
				return;

			List<Map> maps = Find.Maps;

			for(int i = 0; i<maps.Count; i++) {
				maps[i].reservationManager.ReleaseAllForTarget(this);
				maps[i].physicalInteractionReservationManager.ReleaseAllForTarget(this);

				IAttackTarget attackTarget = this as IAttackTarget;
				if(attackTarget!=null)
					maps[i].attackTargetReservationManager.ReleaseAllForTarget(attackTarget);

				//Remove designations on me
				maps[i].designationManager.RemoveAllDesignationsOn(this);
			}
		}

		//=======================================================================================	
		//==================================== Save and Load ====================================
		//=======================================================================================		

		public virtual void ExposeData() {
			Scribe_Defs.Look(ref this.def, "def");

			if(this.def.HasThingIDNumber) {
				string ThingIDTemp = this.ThingID;
				Scribe_Values.Look(ref ThingIDTemp, "id");
				this.ThingID=ThingIDTemp;
			}

			Scribe_Values.Look(ref this.mapIndexOrState, "map", UnspawnedState);

			// reset to Unspawned state if Spawned during loading, because we have to re-Spawn the thing anyway
			if(Scribe.mode==LoadSaveMode.LoadingVars&&this.mapIndexOrState>=0)
				this.mapIndexOrState=UnspawnedState;

			Scribe_Values.Look(ref this.positionInt, "pos", IntVec3.Invalid);   //Dangerous to use PositionInternal?
			Scribe_Values.Look(ref this.rotationInt, "rot", Rot4.North);

			if(this.def.useHitPoints)
				Scribe_Values.Look(ref this.hitPointsInt, "health", -1);

			bool potentiallyTradeable = this.def.tradeability!=Tradeability.None&&this.def.category==ThingCategory.Item;

			if(this.def.stackLimit>1||potentiallyTradeable)
				Scribe_Values.Look(ref this.stackCount, "stackCount", forceSave: true);

			Scribe_Defs.Look(ref this.stuffInt, "stuff");

			//Optimized/hacked faction ref save so it doesn't have to save null for every object
			//Scribe_References.LookReference( ref factionInt, "faction" );
			string facID = (this.factionInt!=null) ? this.factionInt.GetUniqueLoadID() : "null";
			Scribe_Values.Look(ref facID, "faction", defaultValue: "null");
			if(Scribe.mode==LoadSaveMode.LoadingVars||Scribe.mode==LoadSaveMode.ResolvingCrossRefs||Scribe.mode==LoadSaveMode.PostLoadInit) {
				if(facID=="null")
					this.factionInt=null;
				else if(Find.World!=null&&Find.FactionManager!=null)
					this.factionInt=Find.FactionManager.AllFactions.FirstOrDefault(fa => fa.GetUniqueLoadID()==facID);
			}

			Scribe_Collections.Look(ref this.questTags, "questTags", LookMode.Value);

			if(Scribe.mode==LoadSaveMode.PostLoadInit)
				BackCompatibility.ThingPostLoadInit(this);
		}

		public virtual void PostMapInit() {
		}

		//===========================================================================================
		//====================================== Drawing ============================================
		//===========================================================================================

		public virtual Vector3 DrawPos => this.TrueCenter();
		public virtual Color DrawColor {
			get {
				if(this.Stuff!=null)
					return this.Stuff.stuffProps.color;

				if(this.def.graphicData!=null)
					return this.def.graphicData.color;

				return Color.white; //Happens legit on pawns in trade screen
			}
			set =>
				//This is overridden by ThingWithComps, but cannot be used here
				Log.Error("Cannot set instance color on non-ThingWithComps "+this.LabelCap+" at "+this.Position+".");
		}
		public virtual Color DrawColorTwo {
			get {
				if(this.def.graphicData!=null)
					return this.def.graphicData.colorTwo;

				return Color.white;
			}
		}

		/// <summary>
		/// Draw the Thing at its current position. Called each frame for each realtime-drawing Thing on the screen.
		/// For something that can be carried, DrawAt is called instead.
		/// </summary>
		public virtual void Draw() => this.DrawAt(this.DrawPos);

		/// <summary>
		/// Special Draw variant used for drawing while carried. Only necessary for things that can be carried.
		/// </summary>
		public virtual void DrawAt(Vector3 drawLoc, bool flip = false) => this.Graphic.Draw(drawLoc, flip ? this.Rotation.Opposite : this.Rotation, this);

		public virtual void Print(SectionLayer layer) => this.Graphic.Print(layer, this);

		public void DirtyMapMesh(Map map) {
			if(this.def.drawerType!=DrawerType.RealtimeOnly) {
				foreach(IntVec3 c in this.OccupiedRect()) {
					map.mapDrawer.MapMeshDirty(c, MapMeshFlag.Things);
				}
			}
		}

		public virtual void DrawGUIOverlay() {
			if(Find.CameraDriver.CurrentZoom==CameraZoomRange.Closest) {
				if(this.def.stackLimit>1)
					GenMapUI.DrawThingLabel(this, GenString.ToStringCached(this.stackCount));
				else {
					if(this.TryGetQuality(out QualityCategory q))
						GenMapUI.DrawThingLabel(this, q.GetLabelShort());
				}
			}
		}

		public virtual void DrawExtraSelectionOverlays() {
			if(this.def.specialDisplayRadius>0.1f)
				GenDraw.DrawRadiusRing(this.Position, this.def.specialDisplayRadius);

			if(this.def.drawPlaceWorkersWhileSelected&&this.def.PlaceWorkers!=null) {
				for(int i = 0; i<this.def.PlaceWorkers.Count; i++) {
					this.def.PlaceWorkers[i].DrawGhost(this.def, this.Position, this.Rotation, Color.white);
				}
			}

			if(this.def.hasInteractionCell)
				GenDraw.DrawInteractionCell(this.def, this.Position, this.rotationInt);
		}

		//==================================================================================================
		//=========================================== UI ===================================================
		//==================================================================================================

		public virtual string GetInspectString() => "";

		private static List<string> tmpDeteriorationReasons = new List<string>();
		public virtual string GetInspectStringLowPriority() {
			string str = null;

			tmpDeteriorationReasons.Clear();
			SteadyEnvironmentEffects.FinalDeteriorationRate(this, tmpDeteriorationReasons);

			if(tmpDeteriorationReasons.Count!=0)
				str=string.Format("{0}: {1}", "DeterioratingBecauseOf".Translate(), GenText.ToCommaList(tmpDeteriorationReasons).CapitalizeFirst());

			return str;
		}

		public virtual IEnumerable<Gizmo> GetGizmos() { yield break; }
		public virtual IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn) { yield break; }
		public virtual IEnumerable<InspectTabBase> GetInspectTabs() => this.def.inspectorTabsResolved;

		public virtual string GetCustomLabelNoCount(bool includeHp = true) => GenLabel.ThingLabel(this, 1, includeHp: includeHp);

		//===========================================================================================
		//=================================== Damage handling =======================================
		//===========================================================================================	

		public DamageWorker.DamageResult TakeDamage(DamageInfo dinfo) {
			if(this.Destroyed)
				return new DamageWorker.DamageResult();

			if(dinfo.Amount==0)
				return new DamageWorker.DamageResult();

			//Adjust damage amount for damage multipliers
			if(this.def.damageMultipliers!=null) {
				for(int i = 0; i<this.def.damageMultipliers.Count; i++) {
					if(this.def.damageMultipliers[i].damageDef==dinfo.Def) {
						int newAmount = Mathf.RoundToInt(dinfo.Amount*this.def.damageMultipliers[i].multiplier);
						dinfo.SetAmount(newAmount);
					}
				}
			}

			this.PreApplyDamage(ref dinfo, out bool absorbed);

			if(absorbed)
				return new DamageWorker.DamageResult();

			bool spawnedOrAnyParentSpawned = this.SpawnedOrAnyParentSpawned;
			Map mapHeld = this.MapHeld;

			DamageWorker.DamageResult result = dinfo.Def.Worker.Apply(dinfo, this);

			if(dinfo.Def.harmsHealth&&spawnedOrAnyParentSpawned)
				mapHeld.damageWatcher.Notify_DamageTaken(this, result.totalDamageDealt);

			if(dinfo.Def.ExternalViolenceFor(this)) {
				GenLeaving.DropFilthDueToDamage(this, result.totalDamageDealt);

				if(dinfo.Instigator!=null) {
					Pawn pInst = dinfo.Instigator as Pawn;
					if(pInst!=null) {
						pInst.records.AddTo(RecordDefOf.DamageDealt, result.totalDamageDealt);
						pInst.records.AccumulateStoryEvent(StoryEventDefOf.DamageDealt);
					}
				}
			}

			this.PostApplyDamage(dinfo, result.totalDamageDealt);

			return result;
		}

		public virtual void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed) => absorbed=false;
		public virtual void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt) { }

		//===========================================================================================
		//====================================== Stacking ===========================================
		//===========================================================================================	

		/// <summary>
		/// Returns whether this Thing can, in principle, stack with other, ignoring whether or not we are at our stack limit.
		/// </summary>
		public virtual bool CanStackWith(Thing other) {
			if(this.Destroyed||other.Destroyed)
				return false;

			if(this.def.category!=ThingCategory.Item)
				return false;

			return this.def==other.def&&this.Stuff==other.Stuff;
		}

		/// <summary>
		/// Absorbs as much of the other stack as possible.
		/// Returns true if we could absorb the entire other stack and false if we absorbed part of it or none of it.
		/// </summary>
		public virtual bool TryAbsorbStack(Thing other, bool respectStackLimit) {
			if(!this.CanStackWith(other))
				return false;

			int numToTake = ThingUtility.TryAbsorbStackNumToTake(this, other, respectStackLimit);

			//Average health
			if(this.def.useHitPoints)
				this.HitPoints=Mathf.CeilToInt((this.HitPoints*this.stackCount+other.HitPoints*numToTake)/(float)(this.stackCount+numToTake));

			this.stackCount+=numToTake;
			other.stackCount-=numToTake;

			StealAIDebugDrawer.Notify_ThingChanged(this);

			if(this.Spawned)
				this.Map.listerMergeables.Notify_ThingStackChanged(this);

			if(other.stackCount<=0) {
				other.Destroy();
				return true;
			}
			else {
				return false;
			}
		}

		/// <summary>
		/// Returns an unspawned part of this stack split off from the original. Can split off the entire original stack.
		/// Always returns unspawned and unheld Things.
		/// </summary>
		public virtual Thing SplitOff(int count) {
			if(count<=0)
				throw new ArgumentException("SplitOff with count <= 0", "count");

			if(count>=this.stackCount) {
				if(count>this.stackCount)
					Log.Error("Tried to split off "+count+" of "+this+" but there are only "+this.stackCount);

				if(this.Spawned)
					this.DeSpawn();

				if(this.holdingOwner!=null)
					this.holdingOwner.Remove(this);

				return this;
			}

			Thing piece = ThingMaker.MakeThing(this.def, this.Stuff);

			piece.stackCount=count;
			this.stackCount-=count;

			if(this.Spawned)
				this.Map.listerMergeables.Notify_ThingStackChanged(this);

			if(this.def.useHitPoints)
				piece.HitPoints=this.HitPoints;

			return piece;
		}

		//===========================================================================================
		//========================================= Misc ============================================
		//===========================================================================================	

		public virtual IEnumerable<StatDrawEntry> SpecialDisplayStats() {
			yield break;
		}

		public virtual void Notify_ColorChanged() {
			this.graphicInt=null;

			if(this.Spawned
				&&(this.def.drawerType==DrawerType.MapMeshOnly||this.def.drawerType==DrawerType.MapMeshAndRealTime)) {
				this.Map.mapDrawer.MapMeshDirty(this.Position, MapMeshFlag.Things);
			}
		}

		public virtual void Notify_SignalReceived(Signal signal) {
		}

		public virtual TipSignal GetTooltip() {
			string str = this.LabelCap;

			if(this.def.useHitPoints)
				str+="\n"+this.HitPoints+" / "+this.MaxHitPoints;

			return new TipSignal(str, this.thingIDNumber*251235);
		}

		public virtual bool BlocksPawn(Pawn p) => this.def.passability==Traversability.Impassable;

		public void SetFactionDirect(Faction newFaction) {
			if(!this.def.CanHaveFaction) {
				Log.Error("Tried to SetFactionDirect on "+this+" which cannot have a faction.");
				return;
			}

			this.factionInt=newFaction;
		}

		public virtual void SetFaction(Faction newFaction, Pawn recruiter = null) {
			if(!this.def.CanHaveFaction) {
				Log.Error("Tried to SetFaction on "+this+" which cannot have a faction.");
				return;
			}

			this.factionInt=newFaction;

			if(this.Spawned) {
				IAttackTarget attackTarget = this as IAttackTarget;
				if(attackTarget!=null)
					this.Map.attackTargetsCache.UpdateTarget(attackTarget);
			}
		}

		public void SetPositionDirect(IntVec3 newPos) => this.positionInt=newPos;

		public void SetStuffDirect(ThingDef newStuff) => this.stuffInt=newStuff;

		public override string ToString() {
			if(this.def!=null)
				return this.ThingID;
			else
				return this.GetType().ToString();
		}

		public override int GetHashCode() => this.thingIDNumber;

		/// <summary>
		/// If you're calling this method manually then make sure that Destroy() is also called (if not destroyed already).
		/// </summary>
		public virtual void Discard(bool silentlyRemoveReferences = false) {
			if(this.mapIndexOrState!=MemoryState) {
				Log.Warning("Tried to discard "+this+" whose state is "+this.mapIndexOrState+".");
				return;
			}

			this.mapIndexOrState=DiscardedState;
		}

		//===========================================================================================
		//================ Things that should probably get refactored to elsewhere ==================
		//===========================================================================================	

		public virtual IEnumerable<Thing> ButcherProducts(Pawn butcher, float efficiency) {
			if(this.def.butcherProducts!=null) {
				for(int i = 0; i<this.def.butcherProducts.Count; i++) {
					ThingDefCountClass ta = this.def.butcherProducts[i];
					int count = GenMath.RoundRandom(ta.count*efficiency);
					if(count>0) {
						Thing t = ThingMaker.MakeThing(ta.thingDef);
						t.stackCount=count;
						yield return t;
					}
				}
			}
		}

		public const float SmeltCostRecoverFraction = 0.25f;
		public virtual IEnumerable<Thing> SmeltProducts(float efficiency) {
			//Smelt products from my full adjusted cost list including stuff and non-stuff
			List<ThingDefCountClass> costListAdj = this.def.CostListAdjusted(this.Stuff);
			for(int i = 0; i<costListAdj.Count; i++) {
				if(costListAdj[i].thingDef.intricate)
					continue;

				float countF = costListAdj[i].count*SmeltCostRecoverFraction;
				int count = GenMath.RoundRandom(countF);
				if(count>0) {
					Thing t = ThingMaker.MakeThing(costListAdj[i].thingDef);
					t.stackCount=count;
					yield return t;
				}
			}

			//Directly-defined smelt products
			if(this.def.smeltProducts!=null) {
				for(int i = 0; i<this.def.smeltProducts.Count; i++) {
					ThingDefCountClass ta = this.def.smeltProducts[i];
					Thing t = ThingMaker.MakeThing(ta.thingDef);
					t.stackCount=ta.count;
					yield return t;
				}
			}
		}

		public float Ingested(Pawn ingester, float nutritionWanted) {
			if(this.Destroyed) {
				Log.Error(ingester+" ingested destroyed thing "+this);
				return 0;
			}

			if(!this.IngestibleNow) {
				Log.Error(ingester+" ingested IngestibleNow=false thing "+this);
				return 0;
			}

			ingester.mindState.lastIngestTick=Find.TickManager.TicksGame;

			if(this.def.ingestible.outcomeDoers!=null) {
				for(int i = 0; i<this.def.ingestible.outcomeDoers.Count; i++) {
					this.def.ingestible.outcomeDoers[i].DoIngestionOutcome(ingester, this);
				}
			}

			//Basic thought
			if(ingester.needs.mood!=null) {
				List<ThoughtDef> thoughts = FoodUtility.ThoughtsFromIngesting(ingester, this, this.def);
				for(int i = 0; i<thoughts.Count; i++)
					ingester.needs.mood.thoughts.memories.TryGainMemory(thoughts[i]);
			}

			if(ingester.needs.drugsDesire!=null)
				ingester.needs.drugsDesire.Notify_IngestedDrug(this);

			//Record tale as appropriate
			if(ingester.IsColonist&&FoodUtility.IsHumanlikeMeatOrHumanlikeCorpse(this))
				TaleRecorder.RecordTale(TaleDefOf.AteRawHumanlikeMeat, ingester);
			this.IngestedCalculateAmounts(ingester, nutritionWanted, out int numTaken, out float nutritionIngested);

			//Joy impact
			// note that the ingester could die (e.g. due to drug overdose)
			if(!ingester.Dead&&ingester.needs.joy!=null&&Mathf.Abs(this.def.ingestible.joy)>0.0001f&&numTaken>0) {
				JoyKindDef fk = this.def.ingestible.joyKind!=null ? this.def.ingestible.joyKind : JoyKindDefOf.Gluttonous;
				ingester.needs.joy.GainJoy(numTaken*this.def.ingestible.joy, fk);
			}

			//Food poisoning
			if(ingester.RaceProps.Humanlike
			&&Rand.Chance(this.GetStatValue(StatDefOf.FoodPoisonChanceFixedHuman)*Find.Storyteller.difficulty.foodPoisonChanceFactor))
				FoodUtility.AddFoodPoisoningHediff(ingester, this, FoodPoisonCause.DangerousFoodType);

			//Take the stack count
			if(numTaken>0) {
				if(numTaken==this.stackCount)
					this.Destroy();
				else
					this.SplitOff(numTaken); //Discard the split piece; it's ingested.
			}

			this.PostIngested(ingester);

			return nutritionIngested;
		}

		protected virtual void PostIngested(Pawn ingester) {
		}

		protected virtual void IngestedCalculateAmounts(Pawn ingester, float nutritionWanted, out int numTaken, out float nutritionIngested) {
			//Determine num to take
			numTaken=Mathf.CeilToInt(nutritionWanted/this.GetStatValue(StatDefOf.Nutrition));
			numTaken=Mathf.Min(numTaken, this.def.ingestible.maxNumToIngestAtOnce, this.stackCount);
			numTaken=Mathf.Max(numTaken, 1);

			//Determine nutrition to gain
			nutritionIngested=numTaken*this.GetStatValue(StatDefOf.Nutrition);
		}

		public virtual bool PreventPlayerSellingThingsNearby(out string reason) {
			reason=null;
			return false;
		}

		// Note that this is not called for all Thing's - as of this writing, it's only for edifices and blueprints
		public virtual ushort PathFindCostFor(Pawn p) => 0;
	}

}