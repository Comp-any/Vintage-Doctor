using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Vintagestory.Server;

public class ServerSystemRemapperAssistant : ServerSystem
{
	private Vintagestory.API.Datastructures.OrderedDictionary<string, string[]> remaps = new Vintagestory.API.Datastructures.OrderedDictionary<string, string[]>();

	public ServerSystemRemapperAssistant(ServerMain server)
		: base(server)
	{
		server.ModEventManager.AssetsFirstLoaded += AutoRemapBlocksAndItems;
		server.api.ChatCommands.Create("fixmapping").RequiresPrivilege(Privilege.controlserver).BeginSubCommand("doremap")
			.WithDescription("Do remap")
			.WithArgs(server.api.ChatCommands.Parsers.Word("code"))
			.HandleWith(OnCmdDoremap)
			.EndSubCommand()
			.BeginSubCommand("ignoreall")
			.WithDescription("Ignore all remappings")
			.HandleWith(OnCmdIgnoreall)
			.EndSubCommand()
			.BeginSubCommand("applyall")
			.WithDescription("Apply all remappings")
			.WithArgs(server.api.ChatCommands.Parsers.OptionalWord("force"))
			.HandleWith(OnCmdApplyall)
			.EndSubCommand();
	}

	private void AutoRemapBlocksAndItems()
	{
		Dictionary<AssetLocation, Vintagestory.API.Datastructures.OrderedDictionary<string, string[]>> many = server.AssetManager.GetMany<Vintagestory.API.Datastructures.OrderedDictionary<string, string[]>>(ServerMain.Logger, "config/remaps.json");
		remaps = many.First((KeyValuePair<AssetLocation, Vintagestory.API.Datastructures.OrderedDictionary<string, string[]>> v) => v.Key.Domain == "game").Value;
		foreach (KeyValuePair<AssetLocation, Vintagestory.API.Datastructures.OrderedDictionary<string, string[]>> item in many.Where((KeyValuePair<AssetLocation, Vintagestory.API.Datastructures.OrderedDictionary<string, string[]>> v) => v.Key.Domain != "game"))
		{
			foreach (KeyValuePair<string, string[]> item2 in item.Value)
			{
				if (item2.Value.Length != 0)
				{
					if (!remaps.TryGetValue(item2.Key, out var value))
					{
						remaps.Add(item2.Key, item2.Value);
						continue;
					}
					string[] array = value;
					string[] value2 = item2.Value;
					int num = array.Length + value2.Length;
					List<string> list = new List<string>(num);
					CollectionsMarshal.SetCount(list, num);
					Span<string> span = CollectionsMarshal.AsSpan(list);
					int num2 = 0;
					ReadOnlySpan<string> readOnlySpan = new ReadOnlySpan<string>(array);
					readOnlySpan.CopyTo(span.Slice(num2, readOnlySpan.Length));
					num2 += readOnlySpan.Length;
					ReadOnlySpan<string> readOnlySpan2 = new ReadOnlySpan<string>(value2);
					readOnlySpan2.CopyTo(span.Slice(num2, readOnlySpan2.Length));
					num2 += readOnlySpan2.Length;
					List<string> list2 = list;
					remaps[item2.Key] = list2.ToArray();
				}
			}
		}
		if (server.SaveGameData.IsNewWorld)
		{
			foreach (string key in remaps.Keys)
			{
				server.SaveGameData.RemappingsAppliedByCode[key] = true;
			}
			return;
		}
		Vintagestory.API.Datastructures.OrderedDictionary<string, string[]> orderedDictionary = new Vintagestory.API.Datastructures.OrderedDictionary<string, string[]>(remaps);
		foreach (string key2 in server.SaveGameData.RemappingsAppliedByCode.Keys)
		{
			orderedDictionary.Remove(key2);
		}
		if (orderedDictionary.Count > 0)
		{
			if (server.Config.DisableAutoRemap)
			{
				server.requiresRemaps = true;
			}
			else
			{
				AutoRemapBlocksItems(orderedDictionary);
			}
		}
		else
		{
			ServerMain.Logger.Notification("Remapper: No applicable remappings found for this save game, all seems good for now");
		}
	}

	private TextCommandResult OnCmdApplyall(TextCommandCallingArgs args)
	{
		int num = 0;
		int num2 = 0;
		bool flag = args[0] as string == "force";
		IServerPlayer serverPlayer = args.Caller.Player as IServerPlayer;
		if (serverPlayer == null && args.Caller.Type == EnumCallerType.Console)
		{
			serverPlayer = server.ServerConsoleClient.Player;
		}
		foreach (KeyValuePair<string, string[]> remap in remaps)
		{
			string key = remap.Key;
			if (!(!server.SaveGameData.RemappingsAppliedByCode.ContainsKey(key) | flag))
			{
				continue;
			}
			num2++;
			string[] value = remap.Value;
			for (int i = 0; i < value.Length; i++)
			{
				string text = value[i].Trim();
				if (text.Length != 0)
				{
					server.HandleChatMessage(serverPlayer, args.Caller.FromChatGroupId, text);
					num++;
				}
			}
			server.SaveGameData.RemappingsAppliedByCode[key] = true;
		}
		if (num == 0)
		{
			return TextCommandResult.Success("No applicable remappings found, seems all good for now!");
		}
		return TextCommandResult.Success($"Okay, {num2} remapping sets with a total of {num} remappings commands have been executed. You can now restart your game/server");
	}

	private void AutoRemapBlocksItems(Vintagestory.API.Datastructures.OrderedDictionary<string, string[]> remapsToApply)
	{
		ServerSystemBlockIdRemapper serverSystemBlockIdRemapper = server.Systems.First((ServerSystem s) => s is ServerSystemBlockIdRemapper) as ServerSystemBlockIdRemapper;
		ServerSystemItemIdRemapper serverSystemItemIdRemapper = server.Systems.First((ServerSystem s) => s is ServerSystemItemIdRemapper) as ServerSystemItemIdRemapper;
		int num = 0;
		int num2 = 0;
		Dictionary<int, AssetLocation> dictionary = null;
		Dictionary<int, AssetLocation> dictionary2 = null;
		foreach (KeyValuePair<string, string[]> item in remapsToApply)
		{
			num2++;
			string[] value = item.Value;
			for (int num3 = 0; num3 < value.Length; num3++)
			{
				string text = value[num3].Trim();
				if (text.Length == 0)
				{
					continue;
				}
				string[] array = text.Split(" ");
				if (array.Length < 4)
				{
					continue;
				}
				string text2 = array[0];
				if (text2.Equals("/bir"))
				{
					if (dictionary == null)
					{
						dictionary = serverSystemBlockIdRemapper.LoadStoredBlockCodesById();
					}
					if (serverSystemBlockIdRemapper.AutoRemap(array[3], array[2], dictionary))
					{
						num++;
					}
					else
					{
						ServerMain.Logger.Warning("Failed remaps entry (" + array[3] + " not found): " + text);
					}
				}
				else if (text2.Equals("/iir"))
				{
					if (dictionary2 == null)
					{
						dictionary2 = serverSystemItemIdRemapper.LoadStoredItemCodesById();
					}
					if (serverSystemItemIdRemapper.AutoRemap(array[3], array[2], dictionary2))
					{
						num++;
					}
					else
					{
						ServerMain.Logger.Warning("Failed remaps entry (" + array[3] + " not found): " + text);
					}
				}
				else
				{
					ServerMain.Logger.Debug("Unrecognised remaps command: " + text);
				}
			}
			if (dictionary != null)
			{
				serverSystemBlockIdRemapper.StoreBlockCodesById(dictionary);
			}
			if (dictionary2 != null)
			{
				serverSystemItemIdRemapper.StoreItemCodesById(dictionary2);
			}
			server.SaveGameData.RemappingsAppliedByCode[item.Key] = true;
		}
		if (num2 > 0)
		{
			ServerMain.Logger.Notification($"Remapper: Updating a saved world from earlier game version. Applied {num2} remapping sets with a total of {num} remappings.");
		}
	}

	private TextCommandResult OnCmdIgnoreall(TextCommandCallingArgs args)
	{
		foreach (KeyValuePair<string, string[]> remap in remaps)
		{
			string key = remap.Key;
			if (!server.SaveGameData.RemappingsAppliedByCode.ContainsKey(key))
			{
				server.SaveGameData.RemappingsAppliedByCode[key] = false;
			}
		}
		return TextCommandResult.Success(Lang.Get("Okay, ignoring all new remappings. You can still manually remap them using /fixmapping doremap [code]"));
	}

	private TextCommandResult OnCmdDoremap(TextCommandCallingArgs args)
	{
		string key = args[0] as string;
		if (!remaps.ContainsKey(key))
		{
			return TextCommandResult.Success(Lang.Get("No remapping group found under this code"));
		}
		string[] array = remaps[key];
		int num = 0;
		string[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			string text = array2[i].Trim();
			if (text.Length != 0)
			{
				server.HandleChatMessage(args.Caller.Player as IServerPlayer, args.Caller.FromChatGroupId, text);
				num++;
			}
		}
		server.SaveGameData.RemappingsAppliedByCode[key] = true;
		return TextCommandResult.Success(Lang.Get("Ok, {0} commands executed.", num));
	}

	public override void Dispose()
	{
		BlockSchematic.BlockRemaps = null;
		BlockSchematic.ItemRemaps = null;
	}

	public override void OnFinalizeAssets()
	{
		extractRemapsForSchematicImports();
		foreach (KeyValuePair<AssetLocation, string[]> item in server.AssetManager.GetMany<string[]>(ServerMain.Logger, "config/remapentities.json"))
		{
			string[] value = item.Value;
			for (int i = 0; i < value.Length; i++)
			{
				string[] array = value[i].Split(" ");
				if (array[0].Equals("/eir"))
				{
					server.EntityCodeRemappings.TryAdd(array[3], array[2]);
				}
			}
		}
	}

	private void extractRemapsForSchematicImports()
	{
		BlockSchematic.BlockRemaps = new Vintagestory.API.Datastructures.OrderedDictionary<string, Dictionary<string, string>>();
		BlockSchematic.ItemRemaps = new Vintagestory.API.Datastructures.OrderedDictionary<string, Dictionary<string, string>>();
		foreach (KeyValuePair<string, string[]> remap in remaps)
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			Dictionary<string, string> dictionary2 = new Dictionary<string, string>();
			string[] value = remap.Value;
			foreach (string text in value)
			{
				string[] array = text.Split(" ");
				string text2 = array[0];
				if (text2.Equals("/bir"))
				{
					dictionary.TryAdd(array[3], array[2]);
				}
				else if (text2.Equals("/iir"))
				{
					dictionary2.TryAdd(array[3], array[2]);
				}
				else
				{
					ServerMain.Logger.Debug("Unrecognised remaps command: " + text);
				}
			}
			if (dictionary.Count > 0)
			{
				BlockSchematic.BlockRemaps.TryAdd(remap.Key, dictionary);
			}
			if (dictionary2.Count > 0)
			{
				BlockSchematic.ItemRemaps.TryAdd(remap.Key, dictionary2);
			}
		}
	}
}
