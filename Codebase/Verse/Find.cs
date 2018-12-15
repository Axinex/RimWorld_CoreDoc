using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityStandardAssets.ImageEffects;
using Verse.Sound;

namespace Verse {
	public static class Find {
		public static Root Root => Current.Root;

		public static SoundRoot SoundRoot => Current.Root.soundRoot;

		public static UIRoot UIRoot => (!(Current.Root!=null)) ? null : Current.Root.uiRoot;

		public static MusicManagerEntry MusicManagerEntry => ((Root_Entry)Current.Root).musicManagerEntry;

		public static MusicManagerPlay MusicManagerPlay => ((Root_Play)Current.Root).musicManagerPlay;

		public static LanguageWorker ActiveLanguageWorker => LanguageDatabase.activeLanguage.Worker;

		public static Camera Camera => Current.Camera;

		public static CameraDriver CameraDriver => Current.CameraDriver;

		public static ColorCorrectionCurves CameraColor => Current.ColorCorrectionCurves;

		public static Camera PortraitCamera => PortraitCameraManager.PortraitCamera;

		public static PortraitRenderer PortraitRenderer => PortraitCameraManager.PortraitRenderer;

		public static Camera WorldCamera => WorldCameraManager.WorldCamera;

		public static WorldCameraDriver WorldCameraDriver => WorldCameraManager.WorldCameraDriver;

		public static WindowStack WindowStack => (Find.UIRoot==null) ? null : Find.UIRoot.windows;

		public static ScreenshotModeHandler ScreenshotModeHandler => Find.UIRoot.screenshotMode;

		public static MainButtonsRoot MainButtonsRoot => ((UIRoot_Play)Find.UIRoot).mainButtonsRoot;

		public static MainTabsRoot MainTabsRoot => Find.MainButtonsRoot.tabs;

		public static MapInterface MapUI => ((UIRoot_Play)Find.UIRoot).mapUI;

		public static Selector Selector => Find.MapUI.selector;

		public static Targeter Targeter => Find.MapUI.targeter;

		public static ColonistBar ColonistBar => Find.MapUI.colonistBar;

		public static DesignatorManager DesignatorManager => Find.MapUI.designatorManager;

		public static ReverseDesignatorDatabase ReverseDesignatorDatabase => Find.MapUI.reverseDesignatorDatabase;

		public static GameInitData GameInitData => (Current.Game==null) ? null : Current.Game.InitData;

		public static GameInfo GameInfo => Current.Game.Info;

		public static Scenario Scenario {
			get {
				if(Current.Game!=null&&Current.Game.Scenario!=null) {
					return Current.Game.Scenario;
				}
				if(ScenarioMaker.GeneratingScenario!=null) {
					return ScenarioMaker.GeneratingScenario;
				}
				if(Find.UIRoot!=null) {
					Page_ScenarioEditor page_ScenarioEditor = Find.WindowStack.WindowOfType<Page_ScenarioEditor>();
					if(page_ScenarioEditor!=null) {
						return page_ScenarioEditor.EditingScenario;
					}
				}
				return null;
			}
		}

		public static World World => (Current.Game==null||Current.Game.World==null) ? Current.CreatingWorld : Current.Game.World;

		public static List<Map> Maps {
			get {
				if(Current.Game==null) {
					return null;
				}
				return Current.Game.Maps;
			}
		}

		public static Map CurrentMap {
			get {
				if(Current.Game==null) {
					return null;
				}
				return Current.Game.CurrentMap;
			}
		}

		public static Map AnyPlayerHomeMap => Current.Game.AnyPlayerHomeMap;

		public static StoryWatcher StoryWatcher => Current.Game.storyWatcher;

		public static ResearchManager ResearchManager => Current.Game.researchManager;

		public static Storyteller Storyteller {
			get {
				if(Current.Game==null) {
					return null;
				}
				return Current.Game.storyteller;
			}
		}

		public static GameEnder GameEnder => Current.Game.gameEnder;

		public static LetterStack LetterStack => Current.Game.letterStack;

		public static Archive Archive => (Find.History==null) ? null : Find.History.archive;

		public static PlaySettings PlaySettings => Current.Game.playSettings;

		public static History History => (Current.Game==null) ? null : Current.Game.history;

		public static TaleManager TaleManager => Current.Game.taleManager;

		public static PlayLog PlayLog => Current.Game.playLog;

		public static BattleLog BattleLog => Current.Game.battleLog;

		public static TickManager TickManager => Current.Game.tickManager;

		public static Tutor Tutor {
			get {
				if(Current.Game==null) {
					return null;
				}
				return Current.Game.tutor;
			}
		}

		public static TutorialState TutorialState => Current.Game.tutor.tutorialState;

		public static ActiveLessonHandler ActiveLesson {
			get {
				if(Current.Game==null) {
					return null;
				}
				return Current.Game.tutor.activeLesson;
			}
		}

		public static Autosaver Autosaver => Current.Game.autosaver;

		public static DateNotifier DateNotifier => Current.Game.dateNotifier;

		public static SignalManager SignalManager => Current.Game.signalManager;

		public static UniqueIDsManager UniqueIDsManager => (Current.Game==null) ? null : Current.Game.uniqueIDsManager;

		public static FactionManager FactionManager => Find.World.factionManager;

		public static WorldPawns WorldPawns => Find.World.worldPawns;

		public static WorldObjectsHolder WorldObjects => Find.World.worldObjects;

		public static WorldGrid WorldGrid => Find.World.grid;

		public static WorldDebugDrawer WorldDebugDrawer => Find.World.debugDrawer;

		public static WorldPathGrid WorldPathGrid => Find.World.pathGrid;

		public static WorldDynamicDrawManager WorldDynamicDrawManager => Find.World.dynamicDrawManager;

		public static WorldPathFinder WorldPathFinder => Find.World.pathFinder;

		public static WorldPathPool WorldPathPool => Find.World.pathPool;

		public static WorldReachability WorldReachability => Find.World.reachability;

		public static WorldFloodFiller WorldFloodFiller => Find.World.floodFiller;

		public static WorldFeatures WorldFeatures => Find.World.features;

		public static WorldInterface WorldInterface => Find.World.UI;

		public static WorldSelector WorldSelector => Find.WorldInterface.selector;

		public static WorldTargeter WorldTargeter => Find.WorldInterface.targeter;

		public static WorldRoutePlanner WorldRoutePlanner => Find.WorldInterface.routePlanner;
	}
}
