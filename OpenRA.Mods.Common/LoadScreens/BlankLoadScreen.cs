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

using System;
using System.IO;
using System.Threading;
using OpenRA.FileFormats;
using OpenRA.FileSystem;
using OpenRA.Mods.Common.FileSystem;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets.Logic;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.LoadScreens
{
	public class BlankLoadScreen : ILoadScreen
	{
		public LaunchArguments Launch;
		protected IReadOnlyFileSystem fileSystem;
		bool initialized;

		public virtual void Init(Manifest manifest, IReadOnlyFileSystem fileSystem)
		{
			this.fileSystem = fileSystem;
		}

		public virtual void Display()
		{
			if (Game.Renderer == null || initialized)
				return;

			// Draw a black screen
			Game.Renderer.BeginUI();
			Game.Renderer.EndFrame(new NullInputHandler());

			// PERF: draw the screen only once
			initialized = true;
		}

		public virtual void StartGame(Arguments args)
		{
			Launch = new LaunchArguments(args);
			Ui.ResetAll();
			Game.Settings.Save();

			if (!string.IsNullOrEmpty(Launch.Benchmark))
			{
				Console.WriteLine($"Saving benchmark data into {Path.Combine(Platform.SupportDir, "Logs")}");

				Game.BenchmarkMode(Launch.Benchmark);
			}

			// Join a server directly
			var connect = Launch.GetConnectEndPoint();
			if (connect != null)
			{
				Game.LoadShellMap();
				Game.RemoteDirectConnect(connect);
				return;
			}

			// Multi-session RL mode: single process hosts multiple game sessions via gRPC
			if (!string.IsNullOrEmpty(Launch.MultiSession))
			{
				Console.WriteLine("Starting in multi-session RL mode");
				var port = 9999;
				var envPort = Environment.GetEnvironmentVariable("RL_GRPC_PORT");
				if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var p))
					port = p;
				else if (int.TryParse(Launch.MultiSession, out var lp) && lp > 0)
					port = lp;

				RLSessionManager.Initialize(Game.ModData);

				// Start gRPC server on a background thread (blocks that thread)
				var grpcThread = new Thread(() => RLSessionManager.StartGrpcServer(port))
				{
					IsBackground = true,
					Name = "RL-MultiSession-gRPC"
				};
				grpcThread.Start();

				Console.WriteLine($"Multi-session gRPC server started on port {port}");
				Console.WriteLine("Waiting for CreateSession RPCs...");

				// Block the main thread — the gRPC server runs until process exit.
				// Game.Loop() is not used in multi-session mode.
				grpcThread.Join();
				return;
			}

			// Start a map directly
			if (!string.IsNullOrEmpty(Launch.Map))
			{
				Game.LoadMap(Launch.Map, Launch.Bots);
				return;
			}

			// Load a replay directly
			if (!string.IsNullOrEmpty(Launch.Replay))
			{
				ReplayMetadata replayMeta = null;
				try
				{
					replayMeta = ReplayMetadata.Read(Launch.Replay);
				}
				catch { }

				// Headless games (Game.Platform=Null) don't write replay metadata,
				// so replayMeta may be null even for valid replays from the same engine.
				// When Launch.Replay is set, the user explicitly wants to view a replay,
				// so skip the compatibility dialog and play directly.
				if (replayMeta == null)
				{
					Game.JoinReplay(Launch.Replay);
				}
				else if (ReplayUtils.PromptConfirmReplayCompatibility(replayMeta, Game.ModData, Game.LoadShellMap))
				{
					Game.JoinReplay(Launch.Replay);

					var modID = replayMeta.GameInfo.Mod;
					if (modID != null && modID != Game.ModData.Manifest.Id && Game.Mods.TryGetValue(modID, out var mod))
						Game.InitializeMod(mod, args);
				}

				return;
			}

			Game.LoadShellMap();
			Game.Settings.Save();
		}

		protected virtual void Dispose(bool disposing) { }

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		public virtual bool BeforeLoad(ModData modData)
		{
			var graphicSettings = Game.Settings.Graphics;

			// Reset the UI scaling if the user has configured a UI scale that pushes us below the minimum allowed effective resolution
			var minResolution = modData.GetOrCreate<WorldViewportSizes>().MinEffectiveResolution;
			var resolution = Game.Renderer.Resolution;
			if ((resolution.Width < minResolution.Width || resolution.Height < minResolution.Height) && Game.Settings.Graphics.UIScale > 1.0f)
			{
				graphicSettings.UIScale = 1.0f;
				Game.Renderer.SetUIScale(1.0f);
			}

			// Saved settings may have been invalidated by a hardware change
			graphicSettings.VideoDisplay = Game.Renderer.CurrentDisplay;
			if (graphicSettings.GLProfile != GLProfile.Automatic && graphicSettings.GLProfile != Game.Renderer.GLProfile)
				graphicSettings.GLProfile = GLProfile.Automatic;

			if (modData.FileSystemLoader is not IFileSystemExternalContent content)
				return true;

			// Skip content check in headless mode (RL training doesn't need game assets)
			if (Game.IsHeadless)
				return true;

			return !content.InstallContentIfRequired(modData);
		}
	}
}
