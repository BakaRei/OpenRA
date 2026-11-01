#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Threading;
using OpenRA.Primitives;

namespace OpenRA.Support
{
	public static class PerfHistory
	{
		/// <summary>When true, Increment is a no-op. Set in headless/multi-session mode to avoid lock contention.</summary>
		public static volatile bool Disabled;

		static readonly Color[] Colors =
		[
			Color.Red, Color.Green,
			Color.Orange, Color.Yellow,
			Color.Fuchsia, Color.Lime,
			Color.Cyan, Color.Blue,
			Color.White, Color.Teal,
			Color.Pink, Color.MediumPurple,
			Color.Olive, Color.CornflowerBlue
		];

		static int nextColor;
		static readonly Lock SyncRoot = new();

		public static Cache<string, PerfItem> Items = new(
			s =>
			{
				var x = new PerfItem(s, Colors[nextColor++]);
				if (nextColor >= Colors.Length) nextColor = 0;
				return x;
			});

		public static void Increment(string item, double x)
		{
			if (Disabled)
				return;

			lock (SyncRoot)
				Items[item].Val += x;
		}

		public static void Tick(bool gameIsActive = true)
		{
			foreach (var item in Items.Values)
				if (item.HasNormalTick)
					item.Tick(gameIsActive);
		}

		public static void Reset()
		{
			foreach (var item in Items.Values)
				item.ResetSamples();
		}
	}
}
