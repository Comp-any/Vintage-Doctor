using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.Common;

public abstract class EventManager
{
	private long listenerId;

	private long callBackId;

	internal List<GameTickListener> GameTickListenersEntity = new List<GameTickListener>();

	internal ConcurrentDictionary<long, DelayedCallback> DelayedCallbacksEntity = new ConcurrentDictionary<long, DelayedCallback>(4, 16);

	internal List<GameTickListenerBlock> GameTickListenersBlock = new List<GameTickListenerBlock>();

	internal List<DelayedCallbackBlock> DelayedCallbacksBlock = new List<DelayedCallbackBlock>();

	internal ConcurrentDictionary<long, int> GameTickListenersEntityIndices = new ConcurrentDictionary<long, int>(4, 64);

	internal ConcurrentDictionary<long, int> GameTickListenersBlockIndices = new ConcurrentDictionary<long, int>(4, 64);

	internal Dictionary<BlockPos, DelayedCallbackBlock> SingleDelayedCallbacksBlock = new Dictionary<BlockPos, DelayedCallbackBlock>();

	private List<DelayedCallback> deletable = new List<DelayedCallback>();

	public abstract ILogger Logger { get; }

	public abstract string CommandPrefix { get; }

	public abstract long InWorldEllapsedMs { get; }

	public event OnGetClimateDelegate OnGetClimate;

	public event OnGetWindSpeedDelegate OnGetWindSpeed;

	public abstract bool HasPrivilege(string playerUid, string privilegecode);

	public void Trigger<T>(Delegate[] delegates, string eventName, Action<T> onDele, Action onException = null) where T : Delegate
	{
		if (delegates == null)
		{
			return;
		}
		for (int i = 0; i < delegates.Length; i++)
		{
			T obj = (T)delegates[i];
			try
			{
				onDele(obj);
			}
			catch (Exception e)
			{
				Logger.Error("Mod exception during event " + eventName + ". Will skip to next event");
				Logger.Error(e);
				onException?.Invoke();
			}
		}
	}

	public void Trigger<T>(Delegate[] delegates, string eventName, ActionBoolReturn<T> onDele, Action onException = null) where T : Delegate
	{
		if (delegates == null)
		{
			return;
		}
		for (int i = 0; i < delegates.Length; i++)
		{
			T t = (T)delegates[i];
			try
			{
				if (!onDele(t))
				{
					break;
				}
			}
			catch (Exception e)
			{
				Logger.Error("Mod exception during event " + eventName + ". Will skip to next event");
				Logger.Error(e);
				onException?.Invoke();
			}
		}
	}

	public virtual void TriggerOnGetClimate(ref ClimateCondition climate, BlockPos pos, EnumGetClimateMode mode = EnumGetClimateMode.WorldGenValues, double totalDays = 0.0)
	{
		if (OnGetClimate == null)
		{
			return;
		}
		Delegate[] invocationList = OnGetClimate.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			OnGetClimateDelegate onGetClimateDelegate = (OnGetClimateDelegate)invocationList[i];
			try
			{
				onGetClimateDelegate(ref climate, pos, mode, totalDays);
			}
			catch (Exception e)
			{
				Logger.Error("Mod exception during event OnGetClimate. Will skip to next event");
				Logger.Error(e);
			}
		}
	}

	public virtual void TriggerOnGetWindSpeed(Vec3d pos, ref Vec3d windSpeed)
	{
		if (OnGetWindSpeed == null)
		{
			return;
		}
		Delegate[] invocationList = OnGetWindSpeed.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			OnGetWindSpeedDelegate onGetWindSpeedDelegate = (OnGetWindSpeedDelegate)invocationList[i];
			try
			{
				onGetWindSpeedDelegate(pos, ref windSpeed);
			}
			catch (Exception e)
			{
				Logger.Error("Mod exception during event OnGetWindSpeed. Will skip to next event");
				Logger.Error(e);
			}
		}
	}

	public virtual void TriggerGameTick(long ellapsedMilliseconds, IWorldAccessor world)
	{
		FrameProfilerUtil frameProfiler = world.FrameProfiler;
		List<GameTickListener> gameTickListenersEntity = GameTickListenersEntity;
		for (int i = 0; i < gameTickListenersEntity.Count; i++)
		{
			GameTickListener gameTickListener = gameTickListenersEntity[i];
			if (gameTickListener != null && ellapsedMilliseconds - gameTickListener.LastUpdateMilliseconds > gameTickListener.Millisecondinterval)
			{
				gameTickListener.OnTriggered(ellapsedMilliseconds);
			}
		}
		frameProfiler.Mark("tick-gtentity");
		List<GameTickListenerBlock> gameTickListenersBlock = GameTickListenersBlock;
		for (int j = 0; j < gameTickListenersBlock.Count; j++)
		{
			GameTickListenerBlock gameTickListenerBlock = gameTickListenersBlock[j];
			if (gameTickListenerBlock != null && ellapsedMilliseconds - gameTickListenerBlock.LastUpdateMilliseconds > gameTickListenerBlock.Millisecondinterval)
			{
				gameTickListenerBlock.OnTriggered(world, gameTickListenerBlock.Pos, ellapsedMilliseconds);
			}
		}
		frameProfiler.Mark("tick-gtblock");
		deletable.Clear();
		foreach (KeyValuePair<long, DelayedCallback> item in DelayedCallbacksEntity)
		{
			if (ellapsedMilliseconds - item.Value.CallAtEllapsedMilliseconds >= 0)
			{
				DelayedCallback value = item.Value;
				value.Handler((float)(ellapsedMilliseconds - value.CallAtEllapsedMilliseconds) / 1000f);
				deletable.Add(value);
			}
		}
		frameProfiler.Mark("tick-dcentity");
		foreach (DelayedCallback item2 in deletable)
		{
			DelayedCallbacksEntity.TryRemove(item2.ListenerId, out var _);
		}
		List<DelayedCallbackBlock> delayedCallbacksBlock = DelayedCallbacksBlock;
		for (int k = 0; k < delayedCallbacksBlock.Count; k++)
		{
			DelayedCallbackBlock delayedCallbackBlock = delayedCallbacksBlock[k];
			if (ellapsedMilliseconds - delayedCallbackBlock.CallAtEllapsedMilliseconds >= 0)
			{
				delayedCallbacksBlock.RemoveAt(k);
				k--;
				delayedCallbackBlock.Handler(world, delayedCallbackBlock.Pos, (float)(ellapsedMilliseconds - delayedCallbackBlock.CallAtEllapsedMilliseconds) / 1000f);
			}
		}
		Dictionary<BlockPos, DelayedCallbackBlock> singleDelayedCallbacksBlock = SingleDelayedCallbacksBlock;
		if (singleDelayedCallbacksBlock.Count > 0)
		{
			foreach (BlockPos item3 in new List<BlockPos>(singleDelayedCallbacksBlock.Keys))
			{
				DelayedCallbackBlock delayedCallbackBlock2 = singleDelayedCallbacksBlock[item3];
				if (ellapsedMilliseconds - delayedCallbackBlock2.CallAtEllapsedMilliseconds >= 0)
				{
					singleDelayedCallbacksBlock.Remove(item3);
					delayedCallbackBlock2.Handler(world, delayedCallbackBlock2.Pos, (float)(ellapsedMilliseconds - delayedCallbackBlock2.CallAtEllapsedMilliseconds) / 1000f);
				}
			}
		}
		frameProfiler.Mark("tick-dcblock");
	}

	public virtual void TriggerGameTickDebug(long ellapsedMilliseconds, IWorldAccessor world)
	{
		List<GameTickListener> gameTickListenersEntity = GameTickListenersEntity;
		for (int i = 0; i < gameTickListenersEntity.Count; i++)
		{
			GameTickListener gameTickListener = gameTickListenersEntity[i];
			if (gameTickListener != null && ellapsedMilliseconds - gameTickListener.LastUpdateMilliseconds > gameTickListener.Millisecondinterval)
			{
				gameTickListener.OnTriggered(ellapsedMilliseconds);
				world.FrameProfiler.Mark("gmle", gameTickListener.ProfilerName);
			}
		}
		List<GameTickListenerBlock> gameTickListenersBlock = GameTickListenersBlock;
		for (int j = 0; j < gameTickListenersBlock.Count; j++)
		{
			GameTickListenerBlock gameTickListenerBlock = gameTickListenersBlock[j];
			if (gameTickListenerBlock != null && ellapsedMilliseconds - gameTickListenerBlock.LastUpdateMilliseconds > gameTickListenerBlock.Millisecondinterval)
			{
				gameTickListenerBlock.OnTriggered(world, gameTickListenerBlock.Pos, ellapsedMilliseconds);
				world.FrameProfiler.Mark("gmlb", gameTickListenerBlock.ProfilerName);
			}
		}
		deletable.Clear();
		foreach (KeyValuePair<long, DelayedCallback> item in DelayedCallbacksEntity)
		{
			if (ellapsedMilliseconds - item.Value.CallAtEllapsedMilliseconds >= 0)
			{
				DelayedCallback value = item.Value;
				value.Handler((float)(ellapsedMilliseconds - value.CallAtEllapsedMilliseconds) / 1000f);
				deletable.Add(value);
				world.FrameProfiler.Mark("dce", value.ProfileName);
			}
		}
		foreach (DelayedCallback item2 in deletable)
		{
			DelayedCallbacksEntity.TryRemove(item2.ListenerId, out var _);
		}
		List<DelayedCallbackBlock> delayedCallbacksBlock = DelayedCallbacksBlock;
		for (int k = 0; k < delayedCallbacksBlock.Count; k++)
		{
			DelayedCallbackBlock delayedCallbackBlock = delayedCallbacksBlock[k];
			if (ellapsedMilliseconds - delayedCallbackBlock.CallAtEllapsedMilliseconds >= 0)
			{
				delayedCallbacksBlock.RemoveAt(k);
				k--;
				delayedCallbackBlock.Handler(world, delayedCallbackBlock.Pos, (float)(ellapsedMilliseconds - delayedCallbackBlock.CallAtEllapsedMilliseconds) / 1000f);
				world.FrameProfiler.Mark("dcb", delayedCallbackBlock.ProfileName);
			}
		}
		Dictionary<BlockPos, DelayedCallbackBlock> singleDelayedCallbacksBlock = SingleDelayedCallbacksBlock;
		if (singleDelayedCallbacksBlock.Count <= 0)
		{
			return;
		}
		foreach (BlockPos item3 in new List<BlockPos>(singleDelayedCallbacksBlock.Keys))
		{
			DelayedCallbackBlock delayedCallbackBlock2 = singleDelayedCallbacksBlock[item3];
			if (ellapsedMilliseconds - delayedCallbackBlock2.CallAtEllapsedMilliseconds >= 0)
			{
				singleDelayedCallbacksBlock.Remove(item3);
				delayedCallbackBlock2.Handler(world, delayedCallbackBlock2.Pos, (float)(ellapsedMilliseconds - delayedCallbackBlock2.CallAtEllapsedMilliseconds) / 1000f);
				world.FrameProfiler.Mark("sdcb", delayedCallbackBlock2.ProfileName);
			}
		}
	}

	public virtual long AddGameTickListener(Action<float> handler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return AddGameTickListener(handler, null, millisecondInterval, initialDelayOffsetMs);
	}

	public virtual long AddGameTickListener(Action<float> handler, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		long num = ++listenerId;
		GameTickListener gameTickListener = new GameTickListener(num, millisecondInterval, InWorldEllapsedMs + initialDelayOffsetMs, handler, errorHandler);
		List<GameTickListener> gameTickListenersEntity = GameTickListenersEntity;
		for (int i = 0; i < gameTickListenersEntity.Count; i++)
		{
			if (gameTickListenersEntity[i] == null)
			{
				gameTickListenersEntity[i] = gameTickListener;
				GameTickListenersEntityIndices[num] = i;
				if (gameTickListenersEntity[GameTickListenersEntityIndices[num]] != gameTickListener)
				{
					throw new InvalidOperationException("Failed to add listener properly");
				}
				return num;
			}
		}
		gameTickListenersEntity.Add(gameTickListener);
		GameTickListenersEntityIndices[num] = gameTickListenersEntity.Count - 1;
		if (gameTickListenersEntity[GameTickListenersEntityIndices[num]] != gameTickListener)
		{
			throw new InvalidOperationException("Failed to add listener properly");
		}
		return num;
	}

	public virtual long AddGameTickListener(Action<IWorldAccessor, BlockPos, float> handler, BlockPos pos, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		return AddGameTickListener(handler, pos, null, millisecondInterval, initialDelayOffsetMs);
	}

	public virtual long AddGameTickListener(Action<float> handler, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		long newListenerId = ++listenerId;
		GameTickListenerBlock listener = new GameTickListenerBlock(newListenerId, millisecondInterval, InWorldEllapsedMs + initialDelayOffsetMs, pos.Copy(), handler, errorHandler);
		return AddGameTickListenerBlockInternal(listener, newListenerId);
	}

	public virtual long AddGameTickListener(Action<IWorldAccessor, BlockPos, float> handler, BlockPos pos, Action<Exception> errorHandler, int millisecondInterval, int initialDelayOffsetMs = 0)
	{
		long newListenerId = ++listenerId;
		GameTickListenerBlock listener = new GameTickListenerBlock(newListenerId, millisecondInterval, InWorldEllapsedMs + initialDelayOffsetMs, pos.Copy(), handler, errorHandler);
		return AddGameTickListenerBlockInternal(listener, newListenerId);
	}

	private long AddGameTickListenerBlockInternal(GameTickListenerBlock listener, long newListenerId)
	{
		List<GameTickListenerBlock> gameTickListenersBlock = GameTickListenersBlock;
		for (int i = 0; i < gameTickListenersBlock.Count; i++)
		{
			if (gameTickListenersBlock[i] == null)
			{
				gameTickListenersBlock[i] = listener;
				GameTickListenersBlockIndices[newListenerId] = i;
				return newListenerId;
			}
		}
		gameTickListenersBlock.Add(listener);
		GameTickListenersBlockIndices[newListenerId] = gameTickListenersBlock.Count - 1;
		return newListenerId;
	}

	public virtual long AddDelayedCallback(Action<float> handler, long callAfterEllapsedMS)
	{
		long num = Interlocked.Increment(ref callBackId);
		DelayedCallback value = new DelayedCallback(num, InWorldEllapsedMs + callAfterEllapsedMS, handler);
		DelayedCallbacksEntity[num] = value;
		return num;
	}

	public virtual long AddDelayedCallback(Action<IWorldAccessor, BlockPos, float> handler, BlockPos pos, long callAfterEllapsedMS)
	{
		long result = Interlocked.Increment(ref callBackId);
		DelayedCallbacksBlock.Add(new DelayedCallbackBlock(result, InWorldEllapsedMs + callAfterEllapsedMS, pos.Copy(), handler));
		return result;
	}

	internal virtual long AddSingleDelayedCallback(Action<IWorldAccessor, BlockPos, float> handler, BlockPos pos, long callAfterEllapsedMs)
	{
		BlockPos blockPos = pos.Copy();
		long result = Interlocked.Increment(ref callBackId);
		SingleDelayedCallbacksBlock[blockPos] = new DelayedCallbackBlock(result, InWorldEllapsedMs + callAfterEllapsedMs, blockPos, handler);
		return result;
	}

	public void RemoveDelayedCallback(long callbackId)
	{
		if (callbackId == 0L || DelayedCallbacksEntity.TryRemove(callbackId, out var _))
		{
			return;
		}
		foreach (DelayedCallbackBlock item in DelayedCallbacksBlock)
		{
			if (item.ListenerId == callbackId)
			{
				DelayedCallbacksBlock.Remove(item);
				return;
			}
		}
		foreach (KeyValuePair<BlockPos, DelayedCallbackBlock> item2 in SingleDelayedCallbacksBlock)
		{
			if (item2.Value.ListenerId == callbackId)
			{
				SingleDelayedCallbacksBlock.Remove(item2.Key);
				break;
			}
		}
	}

	public void RemoveGameTickListener(long listenerId)
	{
		if (listenerId == 0L)
		{
			return;
		}
		int value2;
		if (GameTickListenersEntityIndices.TryRemove(listenerId, out var value))
		{
			GameTickListener gameTickListener = GameTickListenersEntity[value];
			if (gameTickListener != null && gameTickListener.ListenerId == listenerId)
			{
				GameTickListenersEntity[value] = null;
				return;
			}
		}
		else if (GameTickListenersBlockIndices.TryRemove(listenerId, out value2))
		{
			GameTickListenerBlock gameTickListenerBlock = GameTickListenersBlock[value2];
			if (gameTickListenerBlock != null && gameTickListenerBlock.ListenerId == listenerId)
			{
				GameTickListenersBlock[value2] = null;
				return;
			}
		}
		List<GameTickListener> gameTickListenersEntity = GameTickListenersEntity;
		for (int i = 0; i < gameTickListenersEntity.Count; i++)
		{
			GameTickListener gameTickListener2 = gameTickListenersEntity[i];
			if (gameTickListener2 != null && gameTickListener2.ListenerId == listenerId)
			{
				gameTickListenersEntity[i] = null;
				return;
			}
		}
		List<GameTickListenerBlock> gameTickListenersBlock = GameTickListenersBlock;
		for (int j = 0; j < gameTickListenersBlock.Count; j++)
		{
			GameTickListenerBlock gameTickListenerBlock2 = gameTickListenersBlock[j];
			if (gameTickListenerBlock2 != null && gameTickListenerBlock2.ListenerId == listenerId)
			{
				gameTickListenersBlock[j] = null;
				break;
			}
		}
	}
}
