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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class RLBridgeRegistryTest
	{
		static RLBridgeRegistry<string> TwoPlayers()
		{
			var r = new RLBridgeRegistry<string>();
			r.Register("s1", "Multi0", "b0");
			r.Register("s1", "Multi1", "b1");
			return r;
		}

		[TestCase(TestName = "Lookup by session and player returns that bridge")]
		public void LookupByPlayer()
		{
			var r = TwoPlayers();
			Assert.That(r.TryLookup("s1", "Multi1", out var b), Is.EqualTo(RLLookupStatus.Found));
			Assert.That(b, Is.EqualTo("b1"));
		}

		[TestCase(TestName = "Empty player resolves when the session has exactly one bridge")]
		public void EmptyPlayerSingle()
		{
			var r = new RLBridgeRegistry<string>();
			r.Register("s1", "Multi0", "b0");
			Assert.That(r.TryLookup("s1", "", out var b), Is.EqualTo(RLLookupStatus.Found));
			Assert.That(b, Is.EqualTo("b0"));
		}

		[TestCase(TestName = "Empty player is ambiguous when the session has several bridges")]
		public void EmptyPlayerAmbiguous()
		{
			var r = TwoPlayers();
			Assert.That(r.TryLookup("s1", null, out var b), Is.EqualTo(RLLookupStatus.Ambiguous));
			Assert.That(b, Is.Null);
		}

		[TestCase(TestName = "Empty session id falls back to the first session (legacy)")]
		public void EmptySessionFallback()
		{
			var r = new RLBridgeRegistry<string>();
			r.Register("only", "Multi0", "b0");
			Assert.That(r.TryLookup("", "", out var b), Is.EqualTo(RLLookupStatus.Found));
			Assert.That(b, Is.EqualTo("b0"));
		}

		[TestCase(TestName = "Unknown session and unknown player are distinguished")]
		public void UnknownSessionAndPlayer()
		{
			var r = TwoPlayers();
			Assert.That(r.TryLookup("nope", "Multi0", out _), Is.EqualTo(RLLookupStatus.NoSession));
			Assert.That(r.TryLookup("s1", "Multi7", out _), Is.EqualTo(RLLookupStatus.NoPlayer));
			Assert.That(new RLBridgeRegistry<string>().TryLookup("", "", out _), Is.EqualTo(RLLookupStatus.NoSession));
		}

		[TestCase(TestName = "Removing the last player removes the session")]
		public void RemoveLast()
		{
			var r = TwoPlayers();
			Assert.That(r.Remove("s1", "Multi0"), Is.True);
			Assert.That(r.IsEmpty, Is.False);
			Assert.That(r.Remove("s1", "Multi1"), Is.True);
			Assert.That(r.IsEmpty, Is.True);
			Assert.That(r.Remove("s1", "Multi1"), Is.False);
		}

		[TestCase(TestName = "MoveSession re-keys all players and Players() is sorted")]
		public void MoveAndList()
		{
			var r = new RLBridgeRegistry<string>();
			r.Register("tmp", "Multi2", "b2");
			r.Register("tmp", "Multi0", "b0");
			r.MoveSession("tmp", "final");
			Assert.That(r.Players("final"), Is.EqualTo(new[] { "Multi0", "Multi2" }));
			Assert.That(r.Bridges("final"), Is.EqualTo(new[] { "b0", "b2" }));
			Assert.That(r.Players("tmp"), Is.Empty);
			Assert.That(r.RemoveSession("final"), Is.EqualTo(2));
		}
	}
}
