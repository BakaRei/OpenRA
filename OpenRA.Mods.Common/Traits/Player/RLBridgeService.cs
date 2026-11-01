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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using RLProto = OpenRA.Mods.Common.RL;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// gRPC service implementation for the RL Bridge.
	/// Routes RPCs to the correct ExternalBotBridge session via session_id.
	/// In single-session (legacy) mode, session_id can be empty.
	/// </summary>
	public sealed class RLBridgeService : RLProto.RLBridge.RLBridgeBase
	{
		/// <summary>
		/// Wait for a bridge to become available and activated.
		/// In multi-session mode with session_id, looks up immediately.
		/// In single-session mode, polls until the bridge activates (up to 60s).
		/// </summary>
		static async Task<ExternalBotBridge> WaitForBridge(string sessionId, string player, CancellationToken ct)
		{
			var attempts = string.IsNullOrEmpty(sessionId) ? 600 : 3000;
			for (var i = 0; i < attempts; i++)
			{
				var status = ExternalBotBridge.LookupSession(sessionId, player, out var b);
				if (status == RLLookupStatus.Ambiguous)
					throw new RpcException(new Status(StatusCode.InvalidArgument,
						"player required: session has players " + string.Join(",", ExternalBotBridge.Registry.Players(sessionId))));

				// An unknown player in a populated session is a caller error; give activation 5s first.
				if (status == RLLookupStatus.NoPlayer && i >= 50)
					throw new RpcException(new Status(StatusCode.NotFound, $"no bridge for player '{player}'"));

				if (status == RLLookupStatus.Found && b.IsEnabled)
					return b;

				await Task.Delay(100, ct);
			}

			return null;
		}

		/// <summary>
		/// Bidirectional streaming: game sends observations, agent sends actions.
		/// </summary>
		public override async Task GameSession(
			IAsyncStreamReader<RLProto.AgentAction> requestStream,
			IServerStreamWriter<RLProto.GameObservation> responseStream,
			ServerCallContext context)
		{
			// For streaming, we don't have session_id upfront — use legacy lookup
			var bridge = await WaitForBridge("", "", context.CancellationToken);

			if (bridge == null)
			{
				Log.Write("rl-bridge", "GameSession rejected: bridge not activated within 60s timeout");
				return;
			}

			if (bridge.IsRealtime)
				throw new RpcException(new Status(StatusCode.FailedPrecondition,
					"GameSession is not available in realtime mode; use Observe/IssueCommands"));

			bridge.OnAgentConnected();
			Log.Write("rl-bridge", $"GameSession started for session {bridge.SessionId}");

			var ct = context.CancellationToken;

			try
			{
				// Observation sender: game → agent (runs independently)
				var obsTask = Task.Run(async () =>
				{
					try
					{
						while (!ct.IsCancellationRequested)
						{
							var obs = await bridge.ObservationReader.ReadAsync(ct);
							await responseStream.WriteAsync(obs, ct);
							if (obs.Done)
							{
								Log.Write("rl-bridge", $"Game over: {obs.Result}");
								break;
							}
						}
					}
					catch (OperationCanceledException) { }
					catch (ChannelClosedException) { }
				}, ct);

				// Action receiver: agent → game (runs independently)
				var actionTask = Task.Run(async () =>
				{
					try
					{
						while (await requestStream.MoveNext(ct))
							await bridge.ActionWriter.WriteAsync(requestStream.Current, ct);
					}
					catch (OperationCanceledException) { }
					catch (ChannelClosedException) { }
				}, ct);

				// Exit when either loop ends (game over, disconnect, or cancel)
				await Task.WhenAny(obsTask, actionTask);
			}
			finally
			{
				bridge.OnAgentDisconnected();
				Log.Write("rl-bridge", "GameSession ended");
			}
		}

		/// <summary>
		/// Unary RPC: advance N ticks with optional commands, return observation.
		/// Routes to the correct session via request.SessionId.
		/// </summary>
		public override async Task<RLProto.GameObservation> FastAdvance(
			RLProto.FastAdvanceRequest request,
			ServerCallContext context)
		{
			try
			{
				var bridge = await WaitForBridge(request.SessionId, request.Player, context.CancellationToken);

				if (bridge == null)
					throw new RpcException(new Status(StatusCode.Unavailable,
						$"Bridge not activated within 300s (session_id={request.SessionId})"));

				if (bridge.IsRealtime)
					throw new RpcException(new Status(StatusCode.FailedPrecondition,
						"FastAdvance is disabled in realtime arena mode"));

				return await bridge.RequestFastAdvance(
					request.Ticks, request.Commands, context.CancellationToken,
					request.CheckEventsEvery,
					request.EnabledInterrupts.Count > 0 ? request.EnabledInterrupts : null);
			}
			catch (RpcException)
			{
				throw;
			}
			catch (Exception e)
			{
				Log.Write("rl-bridge", $"FastAdvance error for session {request.SessionId}: {e}");
				throw;
			}
		}

		/// <summary>
		/// Unary RPC: query current game state on demand.
		/// </summary>
		public override Task<RLProto.GameState> GetState(
			RLProto.StateRequest request,
			ServerCallContext context)
		{
			ExternalBotBridge.LookupSession(request.SessionId, request.Player, out var bridge);
			if (bridge == null)
			{
				return Task.FromResult(new RLProto.GameState
				{
					Phase = "no_bridge"
				});
			}

			return Task.FromResult(bridge.GetCurrentState());
		}

		/// <summary>
		/// Create a new game session. Only available in multi-session mode.
		/// </summary>
		public override Task<RLProto.CreateSessionResponse> CreateSession(
			RLProto.CreateSessionRequest request,
			ServerCallContext context)
		{
			if (!ExternalBotBridge.MultiSessionMode)
				throw new RpcException(new Status(StatusCode.Unimplemented,
					"CreateSession is only available in multi-session mode"));

			var sessionId = RLSessionManager.CreateSession(
				request.MapName, request.Bots, request.Seed);

			return Task.FromResult(new RLProto.CreateSessionResponse
			{
				SessionId = sessionId
			});
		}

		/// <summary>
		/// Destroy a game session. Only available in multi-session mode.
		/// </summary>
		public override Task<RLProto.DestroySessionResponse> DestroySession(
			RLProto.DestroySessionRequest request,
			ServerCallContext context)
		{
			if (!ExternalBotBridge.MultiSessionMode)
				throw new RpcException(new Status(StatusCode.Unimplemented,
					"DestroySession is only available in multi-session mode"));

			// Destroy is best-effort — never fail the RPC
			try { RLSessionManager.DestroySession(request.SessionId); }
			catch (Exception e) { Log.Write("rl-bridge", $"DestroySession error: {e}"); }

			return Task.FromResult(new RLProto.DestroySessionResponse());
		}

		static async Task<ExternalBotBridge> RequireRealtimeBridge(string sessionId, string player, CancellationToken ct)
		{
			if (string.IsNullOrEmpty(player))
				throw new RpcException(new Status(StatusCode.InvalidArgument, "player required"));

			var bridge = await WaitForBridge(sessionId, player, ct);
			if (bridge == null)
				throw new RpcException(new Status(StatusCode.Unavailable, $"bridge for {player} not active"));

			if (!bridge.IsRealtime)
				throw new RpcException(new Status(StatusCode.FailedPrecondition,
					"Observe/IssueCommands require realtime mode (arena-agent or Launch.Realtime)"));

			return bridge;
		}

		public override async Task<RLProto.GameObservation> Observe(RLProto.ObserveRequest request, ServerCallContext context)
		{
			var bridge = await RequireRealtimeBridge(request.SessionId, request.Player, context.CancellationToken);
			return await bridge.ObserveAsync(request.IncludeSpatial, context.CancellationToken);
		}

		public override async Task<RLProto.IssueCommandsResponse> IssueCommands(RLProto.IssueCommandsRequest request, ServerCallContext context)
		{
			var bridge = await RequireRealtimeBridge(request.SessionId, request.Player, context.CancellationToken);
			var queued = bridge.EnqueueCommands(request.Commands);
			return new RLProto.IssueCommandsResponse { Queued = queued, Tick = RLArenaMatch.World?.WorldTick ?? 0 };
		}

		public override Task<RLProto.ListPlayersResponse> ListPlayers(RLProto.ListPlayersRequest request, ServerCallContext context)
		{
			return RLArenaMatch.ListPlayersAsync();
		}

		public override Task<RLProto.MatchControlResponse> StartMatch(RLProto.StartMatchRequest request, ServerCallContext context)
		{
			return RLArenaMatch.StartAsync();
		}

		public override Task<RLProto.MatchControlResponse> EndMatch(RLProto.EndMatchRequest request, ServerCallContext context)
		{
			return RLArenaMatch.EndAsync(request.Reason);
		}

		public override Task<RLProto.MatchControlResponse> SetTakeover(RLProto.SetTakeoverRequest request, ServerCallContext context)
		{
			if (string.IsNullOrEmpty(request.Player))
				throw new RpcException(new Status(StatusCode.InvalidArgument, "player required"));

			return RLArenaMatch.SetTakeoverAsync(request.Player, request.Enabled);
		}
	}
}
