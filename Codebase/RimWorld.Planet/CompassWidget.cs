using System;
using UnityEngine;
using Verse;

namespace RimWorld.Planet
{
	[StaticConstructorOnStartup]
	public static class CompassWidget
	{
		private const float Padding = 10f;

		private const float Size = 64f;

		private static readonly Texture2D CompassTex = ContentFinder<Texture2D>.Get("UI/Misc/Compass", true);

		private static float Angle
		{
			get
			{
				Vector2 b = GenWorldUI.WorldToUIPosition(Find.WorldGrid.NorthPolePos);
				Vector2 a = new Vector2((float)UI.screenWidth / 2f, (float)UI.screenHeight / 2f);
				b.y = (float)UI.screenHeight - b.y;
				return a.AngleTo(b);
			}
		}

		public static void CompassOnGUI(ref float curBaseY)
		{
			Vector2 center = new Vector2((float)UI.screenWidth - 10f - 32f, curBaseY - 10f - 32f);
			CompassWidget.CompassOnGUI(center);
			curBaseY -= 84f;
		}

		private static void CompassOnGUI(Vector2 center)
		{
			Widgets.DrawTextureRotated(center, CompassWidget.CompassTex, CompassWidget.Angle, 1f);
			Rect rect = new Rect(center.x - 32f, center.y - 32f, 64f, 64f);
			TooltipHandler.TipRegion(rect, "CompassTip".Translate());
			if (Widgets.ButtonInvisible(rect, false))
			{
				Find.WorldCameraDriver.RotateSoNorthIsUp(true);
			}
		}
	}
}
