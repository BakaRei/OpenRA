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
using RLProto = OpenRA.Mods.Common.RL;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class ActionHandlerRejectTest
	{
		[TestCase(RLProto.ActionType.Move, TestName = "Moving another player's unit is rejected")]
		[TestCase(RLProto.ActionType.Attack, TestName = "Attacking with another player's unit is rejected")]
		[TestCase(RLProto.ActionType.Deploy, TestName = "Deploying another player's MCV is rejected")]
		[TestCase(RLProto.ActionType.Sell, TestName = "Selling another player's building is rejected")]
		public void ForeignSubject(RLProto.ActionType action)
		{
			Assert.That(ActionHandler.RejectReason(action, true, "Multi1", "Multi0", false), Is.EqualTo("not_owner"));
		}

		[TestCase(TestName = "Own unit is allowed")]
		public void OwnSubject()
		{
			Assert.That(ActionHandler.RejectReason(RLProto.ActionType.Move, true, "Multi0", "Multi0", false), Is.Null);
		}

		[TestCase(TestName = "Missing subject is left to the existing null checks")]
		public void MissingSubject()
		{
			Assert.That(ActionHandler.RejectReason(RLProto.ActionType.Move, false, null, "Multi0", false), Is.Null);
		}

		[TestCase(TestName = "Production commands have no subject and are allowed")]
		public void Production()
		{
			Assert.That(ActionHandler.RejectReason(RLProto.ActionType.Build, false, null, "Multi0", true), Is.Null);
			Assert.That(ActionHandler.RejectReason(RLProto.ActionType.PlaceBuilding, false, null, "Multi0", true), Is.Null);
		}

		[TestCase(TestName = "Patrol is unsupported by the engine")]
		public void Patrol()
		{
			Assert.That(ActionHandler.RejectReason(RLProto.ActionType.Patrol, true, "Multi0", "Multi0", false), Is.EqualTo("unsupported_action"));
		}

		[TestCase(TestName = "FastAdvance and Surrender are unsupported in realtime mode only")]
		public void RealtimeOnly()
		{
			Assert.That(ActionHandler.RejectReason(RLProto.ActionType.FastAdvance, false, null, "Multi0", true), Is.EqualTo("unsupported_action"));
			Assert.That(ActionHandler.RejectReason(RLProto.ActionType.Surrender, false, null, "Multi0", true), Is.EqualTo("unsupported_action"));
			Assert.That(ActionHandler.RejectReason(RLProto.ActionType.FastAdvance, false, null, "Multi0", false), Is.Null);
		}
	}
}
