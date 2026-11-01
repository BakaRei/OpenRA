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

using System.IO;
using System.Text.Json;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class RLEventLogTest
	{
		[TestCase(TestName = "FormatEvent writes tick, type and typed fields as one JSON object")]
		public void FormatTyped()
		{
			var line = RLEventLog.FormatEvent(1234, "actor_killed",
				("actor_id", 55u), ("owner", "Multi1"), ("is_building", true), ("cost", 700), ("killer_owner", null));
			using var doc = JsonDocument.Parse(line);
			var r = doc.RootElement;
			Assert.That(r.GetProperty("t").GetInt32(), Is.EqualTo(1234));
			Assert.That(r.GetProperty("type").GetString(), Is.EqualTo("actor_killed"));
			Assert.That(r.GetProperty("actor_id").GetUInt32(), Is.EqualTo(55u));
			Assert.That(r.GetProperty("owner").GetString(), Is.EqualTo("Multi1"));
			Assert.That(r.GetProperty("is_building").GetBoolean(), Is.True);
			Assert.That(r.GetProperty("cost").GetInt32(), Is.EqualTo(700));
			Assert.That(r.GetProperty("killer_owner").ValueKind, Is.EqualTo(JsonValueKind.Null));
			Assert.That(line, Does.Not.Contain("\n"));
		}

		[TestCase(TestName = "FormatEvent keeps CJK text unescaped")]
		public void FormatCjk()
		{
			var line = RLEventLog.FormatEvent(1, "match_ended", ("reason", "时间到"));
			Assert.That(line, Does.Contain("时间到"));
		}

		[TestCase(TestName = "Emit appends lines only while open")]
		public void EmitToFile()
		{
			var path = Path.GetTempFileName();
			RLEventLog.Emit(1, "ignored");
			RLEventLog.Open(path);
			Assert.That(RLEventLog.Enabled, Is.True);
			RLEventLog.Emit(2, "match_started");
			RLEventLog.CommandRejected(3, "Multi0", "Move", 9u, "not_owner");
			RLEventLog.Close();
			Assert.That(RLEventLog.Enabled, Is.False);
			var lines = File.ReadAllLines(path);
			Assert.That(lines.Length, Is.EqualTo(2));
			using var doc = JsonDocument.Parse(lines[1]);
			Assert.That(doc.RootElement.GetProperty("reason").GetString(), Is.EqualTo("not_owner"));
			File.Delete(path);
		}

		[TestCase(TestName = "Takeover event carries player and enabled flag")]
		public void TakeoverEvent()
		{
			var path = Path.GetTempFileName();
			RLEventLog.Open(path);
			RLEventLog.Takeover(250, "Multi2", true);
			RLEventLog.Close();
			using var doc = JsonDocument.Parse(File.ReadAllLines(path)[0]);
			var r = doc.RootElement;
			Assert.That(r.GetProperty("t").GetInt32(), Is.EqualTo(250));
			Assert.That(r.GetProperty("type").GetString(), Is.EqualTo("takeover"));
			Assert.That(r.GetProperty("player").GetString(), Is.EqualTo("Multi2"));
			Assert.That(r.GetProperty("enabled").GetBoolean(), Is.True);
			File.Delete(path);
		}
	}
}
