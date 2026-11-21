using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using Mono.Cecil;
using Newtonsoft.Json;
using ProperVersion;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace Vintagestory.Common;

public class ModContainer : Mod
{
	public List<string> MissingDependencies;

	private string selectedAssemblyFile;

	private static HashAlgorithm fileHasher = SHA1.Create();

	public bool Enabled => Status == ModStatus.Enabled;

	public ModStatus Status { get; set; } = ModStatus.Enabled;

	public ModError? Error { get; set; }

	public string FolderPath { get; private set; }

	public List<string> SourceFiles { get; } = new List<string>();

	public List<string> AssemblyFiles { get; } = new List<string>();

	public bool RequiresCompilation => SourceFiles.Count > 0;

	public Assembly Assembly { get; private set; }

	public ModContainer(FileSystemInfo fsInfo, ILogger parentLogger, bool logDebug)
	{
		SourceType = GetSourceType(fsInfo).Value;
		FileName = fsInfo.Name;
		SourcePath = fsInfo.FullName;
		Logger = new ModLogger(parentLogger, this);
		Logger.TraceLog = logDebug;
		switch (SourceType)
		{
		case EnumModSourceType.CS:
			SourceFiles.Add(SourcePath);
			break;
		case EnumModSourceType.DLL:
			AssemblyFiles.Add(SourcePath);
			break;
		case EnumModSourceType.Folder:
			FolderPath = SourcePath;
			break;
		case EnumModSourceType.ZIP:
			break;
		}
	}

	public static EnumModSourceType? GetSourceType(FileSystemInfo fsInfo)
	{
		if (fsInfo is DirectoryInfo)
		{
			return EnumModSourceType.Folder;
		}
		return GetSourceTypeFromExtension(fsInfo.Name);
	}

	private static EnumModSourceType? GetSourceTypeFromExtension(string fileName)
	{
		string extension = Path.GetExtension(fileName);
		if (string.IsNullOrEmpty(extension))
		{
			return null;
		}
		extension = extension.Substring(1).ToUpperInvariant();
		if (!Enum.TryParse<EnumModSourceType>(extension, out var result))
		{
			return null;
		}
		return result;
	}

	public void SetError(ModError error)
	{
		Status = ModStatus.Errored;
		Error = error;
	}

	public static IEnumerable<string> EnumerateModFiles(string path)
	{
		IEnumerable<string> enumerable = Directory.EnumerateFiles(path);
		foreach (string item in Directory.EnumerateDirectories(path))
		{
			if (!(Path.GetFileName(item) == ".git"))
			{
				enumerable = enumerable.Concat(EnumerateModFiles(item));
			}
		}
		return enumerable;
	}

	public void Unpack(string unpackPath)
	{
		if (!Enabled || SourceFiles.Count > 0 || AssemblyFiles.Count > 0)
		{
			return;
		}
		if (SourceType == EnumModSourceType.ZIP)
		{
			using (FileStream inputStream = File.OpenRead(SourcePath))
			{
				byte[] source = fileHasher.ComputeHash(inputStream);
				StringBuilder stringBuilder = new StringBuilder(12);
				foreach (byte item in source.Take(6))
				{
					stringBuilder.Append(item.ToString("x2"));
				}
				FolderPath = Path.Combine(unpackPath, FileName + "_" + stringBuilder.ToString());
			}
			string fullPath = Path.GetFullPath(FolderPath);
			if (!Directory.Exists(FolderPath))
			{
				try
				{
					Directory.CreateDirectory(FolderPath);
					using ZipFile zipFile = new ZipFile(SourcePath);
					foreach (ZipEntry item2 in zipFile)
					{
						string path = Path.Combine(FolderPath, item2.Name);
						path = Path.GetFullPath(path);
						if (!path.StartsWith(fullPath))
						{
							throw new Exception("This mod tried to escape its extraction directory by extracting to '" + path + "'!");
						}
						if (item2.IsDirectory)
						{
							Directory.CreateDirectory(path);
							continue;
						}
						string directoryName = Path.GetDirectoryName(path);
						if (!Directory.Exists(directoryName))
						{
							Directory.CreateDirectory(directoryName);
						}
						using Stream stream = zipFile.GetInputStream(item2);
						using FileStream destination = new FileStream(path, FileMode.Create);
						stream.CopyTo(destination);
					}
				}
				catch (Exception e)
				{
					Logger.Error("An exception was thrown when trying to extract the mod archive to '{0}':", FolderPath);
					Logger.Error(e);
					SetError(ModError.Loading);
					try
					{
						Directory.Delete(FolderPath, recursive: true);
						return;
					}
					catch (Exception)
					{
						Logger.Error("Additionally, there was an exception when deleting cached mod folder path '{0}':", FolderPath);
						Logger.Error(e);
						return;
					}
				}
			}
		}
		string text = Path.Combine(FolderPath, ".ignore");
		IgnoreFile ignoreFile = (File.Exists(text) ? new IgnoreFile(text, FolderPath) : null);
		foreach (string item3 in EnumerateModFiles(FolderPath))
		{
			if (ignoreFile != null && !ignoreFile.Available(item3))
			{
				continue;
			}
			EnumModSourceType? sourceTypeFromExtension = GetSourceTypeFromExtension(item3);
			string text2 = item3.Substring(FolderPath.Length + 1);
			int num = text2.IndexOfAny(new char[2] { '/', '\\' });
			string text3 = ((num >= 0) ? text2.Substring(0, num) : null);
			switch (sourceTypeFromExtension)
			{
			case EnumModSourceType.CS:
				if (text3 != "src")
				{
					Logger.Error("File '{0}' is not in the 'src/' subfolder.", Path.GetFileName(item3));
					if (SourceType != EnumModSourceType.Folder)
					{
						SetError(ModError.Loading);
						return;
					}
				}
				else
				{
					SourceFiles.Add(item3);
				}
				break;
			case EnumModSourceType.DLL:
				if (text3 == "native")
				{
					break;
				}
				if (text3 != null)
				{
					Logger.Error("File '{0}' is not in the mod's root folder. Won't load this mod. If you need to ship unmanaged dlls, put them in the native/ folder.", Path.GetFileName(item3));
					if (SourceType != EnumModSourceType.Folder)
					{
						SetError(ModError.Loading);
						return;
					}
				}
				else
				{
					AssemblyFiles.Add(item3);
				}
				break;
			}
		}
	}

	public void LoadModInfo(ModCompilationContext compilationContext, ModAssemblyLoader loader)
	{
		if (!Enabled || Info != null)
		{
			return;
		}
		try
		{
			if (SourceType == EnumModSourceType.ZIP || SourceType == EnumModSourceType.Folder)
			{
				if (FolderPath != null)
				{
					string path = Path.Combine(FolderPath, "modinfo.json");
					if (File.Exists(path))
					{
						string value = File.ReadAllText(path);
						Info = JsonConvert.DeserializeObject<ModInfo>(value);
						Info?.Init();
					}
					if (Info?.BackgroundPaths != null)
					{
						Info.Backgrounds = new BitmapSupplierDelegate[Info.BackgroundPaths.Length];
						for (int i = 0; i < Info.BackgroundPaths.Length; i++)
						{
							string path2 = Info.BackgroundPaths[i];
							if (RuntimeEnv.IsDevEnvironment)
							{
								_003C_003Ey__InlineArray6<string> buffer = default;
								buffer[0] = FolderPath;
								buffer[1] = "..";
								buffer[2] = "..";
								buffer[3] = "..";
								buffer[4] = "..";
								buffer[5] = path2;
								string p = Path.Combine(buffer);
								Info.Backgrounds[i] = () => new BitmapExternal(p);
							}
							else
							{
								Info.Backgrounds[i] = () => new BitmapExternal(Path.Combine(FolderPath, path2));
							}
						}
					}
					string path3 = Path.Combine(FolderPath, "worldconfig.json");
					if (File.Exists(path3))
					{
						string value2 = File.ReadAllText(path3);
						WorldConfig = JsonConvert.DeserializeObject<ModWorldConfiguration>(value2);
					}
					string text = null;
					if (!string.IsNullOrWhiteSpace(Info?.IconPath))
					{
						try
						{
							text = Path.GetFullPath(Path.Combine(FolderPath, Info.IconPath));
						}
						catch (Exception ex)
						{
							Logger.Warning("Failed create path from the IconPath '{0}' specified in the ModInfo, did you use characters that are not valid in a path?: {1}.", Info.IconPath, ex);
							text = null;
						}
						if (!text.StartsWithOrdinal(FolderPath))
						{
							Logger.Warning("The IconPath '{0}' specified in the ModInfo tried to escape the mod root. This is not allowed.", Info.IconPath);
							text = null;
						}
					}
					else
					{
						text = Path.Combine(FolderPath, "modicon.png");
						if (!File.Exists(text))
						{
							text = GetFallbackIconPath();
						}
					}
					if (File.Exists(text))
					{
						Icon = new BitmapExternal(text, Logger);
					}
				}
				else
				{
					using (ZipFile zipFile = new ZipFile(SourcePath))
					{
						ZipEntry entry = zipFile.GetEntry("modinfo.json");
						if (entry != null)
						{
							using StreamReader streamReader = new StreamReader(zipFile.GetInputStream(entry));
							string value3 = streamReader.ReadToEnd();
							Info = JsonConvert.DeserializeObject<ModInfo>(value3);
							Info?.Init();
						}
						if (Info.BackgroundPaths != null)
						{
							Info.Backgrounds = new BitmapSupplierDelegate[Info.BackgroundPaths.Length];
							for (int num = 0; num < Info.BackgroundPaths.Length; num++)
							{
								string path4 = Info.BackgroundPaths[num];
								Info.Backgrounds[num] = () => loadBackground(path4);
							}
						}
						ZipEntry entry2 = zipFile.GetEntry("worldconfig.json");
						if (entry2 != null)
						{
							using StreamReader streamReader2 = new StreamReader(zipFile.GetInputStream(entry2));
							string value4 = streamReader2.ReadToEnd();
							WorldConfig = JsonConvert.DeserializeObject<ModWorldConfiguration>(value4);
						}
						if (!string.IsNullOrWhiteSpace(Info?.IconPath))
						{
							ZipEntry entry3 = zipFile.GetEntry(Info.IconPath);
							if (entry3 != null)
							{
								using MemoryStream memoryStream = new MemoryStream(1048576);
								using Stream stream = zipFile.GetInputStream(entry3);
								stream.CopyTo(memoryStream);
								Icon = new BitmapExternal(memoryStream, Logger);
							}
							else
							{
								Logger.Warning("Failed find the IconPath '{0}' specified in the ModInfo within the mod archive.", Info.IconPath);
							}
						}
						else
						{
							ZipEntry entry4 = zipFile.GetEntry("modicon.png");
							if (entry4 != null)
							{
								using MemoryStream memoryStream2 = new MemoryStream(1048576);
								using Stream stream2 = zipFile.GetInputStream(entry4);
								stream2.CopyTo(memoryStream2);
								Icon = new BitmapExternal(memoryStream2, Logger);
							}
							else
							{
								string text2 = GetFallbackIconPath();
								if (File.Exists(text2))
								{
									Icon = new BitmapExternal(text2, Logger);
								}
							}
						}
					}
					if (WorldConfig != null)
					{
						Unpack(Path.Combine(GamePaths.Cache, "unpack"));
					}
				}
				if (Info == null)
				{
					Logger.Error("Missing modinfo.json");
					SetError(ModError.Loading);
				}
				if (WorldConfig != null)
				{
					Lang.PreLoadModWorldConfig(FolderPath, Info?.ModID, Lang.CurrentLocale);
				}
			}
			else
			{
				Info = LoadModInfoFromCode(compilationContext, loader, out var modWorldConfig);
				Info?.Init();
				WorldConfig = modWorldConfig;
				if (Info == null)
				{
					Logger.Error("Missing ModInfoAttribute");
					SetError(ModError.Loading);
				}
				string text3 = null;
				if (!string.IsNullOrWhiteSpace(Info?.IconPath))
				{
					try
					{
						text3 = Path.GetFullPath(Path.Combine(GamePaths.AssetsPath, Info.IconPath));
					}
					catch (Exception ex2)
					{
						Logger.Warning("Failed create path from the IconPath '{0}' specified in the ModInfo, did you use characters that are not valid in a path?: {1}.", Info.IconPath, ex2);
						text3 = null;
					}
					if (!text3.StartsWithOrdinal(GamePaths.AssetsPath))
					{
						Logger.Warning("The IconPath '{0}' specified in the ModInfo tried to escape the AssetPath. This is not allowed.", Info.IconPath);
						text3 = null;
					}
				}
				else
				{
					text3 = GetFallbackIconPath();
				}
				if (File.Exists(text3))
				{
					Icon = new BitmapExternal(text3, Logger);
				}
			}
			if (Info != null)
			{
				CheckProperVersions();
			}
		}
		catch (Exception e)
		{
			Logger.Error("An exception was thrown trying to to load the ModInfo:");
			Logger.Error(e);
			SetError(ModError.Loading);
		}
		static string GetFallbackIconPath()
		{
			return Path.Combine(GamePaths.AssetsPath, "game/textures/gui/3rdpartymodicon.png");
		}
	}

	private BitmapExternal loadBackground(string path)
	{
		using ZipFile zipFile = new ZipFile(SourcePath);
		ZipEntry entry = zipFile.GetEntry(path);
		if (entry != null)
		{
			using (MemoryStream memoryStream = new MemoryStream(1048576))
			{
				using Stream stream = zipFile.GetInputStream(entry);
				stream.CopyTo(memoryStream);
				return new BitmapExternal(memoryStream, Logger);
			}
		}
		return null;
	}

	private ModInfo LoadModInfoFromCode(ModCompilationContext compilationContext, ModAssemblyLoader loader, out ModWorldConfiguration modWorldConfig)
	{
		if (RequiresCompilation)
		{
			if (AssemblyFiles.Count > 0)
			{
				throw new Exception("Found both .cs and .dll files, this is not supported");
			}
			Assembly = compilationContext.CompileFromFiles(this);
			Logger.Notification("Successfully compiled {0} source files", SourceFiles.Count);
			Logger.VerboseDebug("Successfully compiled {0} source files", SourceFiles.Count);
			return LoadModInfoFromAssembly(Assembly, out modWorldConfig);
		}
		List<string> list = new List<string>(AssemblyFiles.Count);
		foreach (string assemblyFile in AssemblyFiles)
		{
			list.Add(GamePaths.RemoveUsername(assemblyFile));
		}
		Logger.VerboseDebug("Check for mod systems in mod {0}", string.Join(", ", list));
		List<string> list2 = AssemblyFiles.Where((string file) => isEligible(file)).ToList();
		if (list2.Count == 0)
		{
			throw new Exception(string.Format("{0} declared as code mod, but there are no .dll files that contain at least one ModSystem or has a ModInfo attribute", string.Join(", ", AssemblyFiles)));
		}
		if (list2.Count >= 2)
		{
			throw new Exception("Found multiple .dll files with ModSystems and/or ModInfo attributes");
		}
		selectedAssemblyFile = list2[0];
		Logger.VerboseDebug("Selected assembly {0}", GamePaths.RemoveUsername(selectedAssemblyFile));
		return LoadModInfoFromAssemblyDefinition(loader.LoadAssemblyDefinition(selectedAssemblyFile), out modWorldConfig);
		bool isEligible(string path)
		{
			AssemblyDefinition assemblyDefinition = loader.LoadAssemblyDefinition(path);
			if (assemblyDefinition.CustomAttributes.Any((CustomAttribute attribute) => attribute.AttributeType.Name == "ModInfoAttribute"))
			{
				return assemblyDefinition.Modules.SelectMany((ModuleDefinition module) => module.Types).Any((TypeDefinition type) => !type.IsAbstract && isModSystem(type));
			}
			return false;
		}
		static bool isModSystem(TypeDefinition typeDefinition)
		{
			for (TypeReference typeReference = typeDefinition.BaseType; typeReference != null; typeReference = typeReference.Resolve()?.BaseType)
			{
				if (typeReference.FullName == typeof(ModSystem).FullName)
				{
					return true;
				}
			}
			return false;
		}
	}

	public void LoadAssembly(ModCompilationContext compilationContext, ModAssemblyLoader loader)
	{
		EnumModType enumModType = Info?.Type ?? EnumModType.Code;
		if (!Enabled || Assembly != null)
		{
			return;
		}
		if (enumModType != EnumModType.Code)
		{
			if (SourceFiles.Count > 0 || AssemblyFiles.Count > 0)
			{
				Logger.Warning("Is a {0} mod, but .cs or .dll files were found. These will be ignored.", enumModType);
			}
			return;
		}
		string errorPath = "<unknown>";
		try
		{
			if (RequiresCompilation)
			{
				Assembly = compilationContext.CompileFromFiles(this);
				Logger.Notification("Successfully compiled {0} source files", SourceFiles.Count);
				Logger.VerboseDebug("Successfully compiled {0} source files", SourceFiles.Count);
				return;
			}
			if (selectedAssemblyFile != null)
			{
				errorPath = selectedAssemblyFile;
				Assembly = loader.LoadFrom(selectedAssemblyFile);
				return;
			}
			Logger.VerboseDebug("Check for mod systems in mod {0}", string.Join(", ", AssemblyFiles));
			List<Assembly> list = (from ass in AssemblyFiles.Select((string path) =>
				{
					errorPath = path;
					return loader.LoadFrom(path);
				})
				where ass.GetCustomAttribute<ModInfoAttribute>() != null || GetModSystems(ass).Any()
				select ass).ToList();
			if (list.Count == 0)
			{
				throw new Exception(string.Format("{0} declared as code mod, but there are no .dll files that contain at least one ModSystem or has a ModInfo attribute", string.Join(", ", AssemblyFiles)));
			}
			if (list.Count >= 2)
			{
				throw new Exception("Found multiple .dll files with ModSystems and/or ModInfo attributes");
			}
			Assembly = list[0];
			Logger.VerboseDebug("Loaded assembly {0}", Assembly.Location);
		}
		catch (Exception ex)
		{
			if (ex.Message == "Assembly with same name is already loaded")
			{
				string message = "Please restart the game. The mod's .dll was already loaded and cannot be reloaded. Most likely cause is switching mod versions after already playing one world. Other rare causes include two mods with .dlls with the same name. Mod filename is " + FileName + ". Erroring path is: " + errorPath;
				Logger.Error(message);
				SetError(ModError.ChangedVersion);
				throw new Exception(message, ex);
			}
			Logger.Error("An exception was thrown when trying to load assembly:");
			Logger.Error(ex);
			SetError(ModError.Loading);
		}
	}

	public void InstantiateModSystems(EnumAppSide side)
	{
		if (!Enabled || Assembly == null || Systems.Count > 0)
		{
			return;
		}
		if (Info == null)
		{
			throw new InvalidOperationException("LoadModInfo was not called before InstantiateModSystems");
		}
		if (!Info.Side.Is(side))
		{
			Status = ModStatus.Unused;
			return;
		}
		List<ModSystem> list = new List<ModSystem>();
		foreach (Type modSystem2 in GetModSystems(Assembly))
		{
			try
			{
				ModSystem modSystem = (ModSystem)Activator.CreateInstance(modSystem2);
				modSystem.Mod = this;
				list.Add(modSystem);
			}
			catch (Exception e)
			{
				Logger.Error("Exception thrown when trying to create an instance of ModSystem {0}:", modSystem2);
				Logger.Error(e);
			}
		}
		Systems = list.AsReadOnly();
		if (Systems.Count == 0 && FolderPath == null)
		{
			Logger.Warning("Is a Code mod, but no ModSystems found");
		}
	}

	private IEnumerable<Type> GetModSystems(Assembly assembly)
	{
		try
		{
			return from type in assembly.GetTypes()
				where typeof(ModSystem).IsAssignableFrom(type) && !type.IsAbstract
				select type;
		}
		catch (Exception ex)
		{
			if (ex is ReflectionTypeLoadException)
			{
				Exception[] loaderExceptions = (ex as ReflectionTypeLoadException).LoaderExceptions;
				Logger.Error("Exception thrown when attempting to retrieve all types of the assembly {0}. Will ignore asssembly. Loader exceptions:", assembly.FullName);
				Logger.Error(ex);
				if (ex.InnerException != null)
				{
					Logger.Error("InnerException:");
					Logger.Error(ex.InnerException);
				}
				for (int num = 0; num < loaderExceptions.Length; num++)
				{
					Logger.Error(loaderExceptions[num]);
				}
			}
			else
			{
				Logger.Error("Exception thrown when attempting to retrieve all types of the assembly {0}: {1}, InnerException: {2}. Will ignore asssembly.", assembly.FullName, ex, ex.InnerException);
			}
			return Enumerable.Empty<Type>();
		}
	}

	private ModInfo LoadModInfoFromAssembly(Assembly assembly, out ModWorldConfiguration modWorldConfig)
	{
		ModInfoAttribute customAttribute = assembly.GetCustomAttribute<ModInfoAttribute>();
		if (customAttribute == null)
		{
			modWorldConfig = null;
			return null;
		}
		List<ModDependency> dependencies = (from attr in assembly.GetCustomAttributes<ModDependencyAttribute>()
			select new ModDependency(attr.ModID, attr.Version)).ToList();
		return LoadModInfoFromModInfoAttribute(customAttribute, dependencies, out modWorldConfig);
	}

	private ModInfo LoadModInfoFromAssemblyDefinition(AssemblyDefinition assemblyDefinition, out ModWorldConfiguration modWorldConfig)
	{
		CustomAttribute customAttribute = assemblyDefinition.CustomAttributes.SingleOrDefault((CustomAttribute attribute) => attribute.AttributeType.Name == "ModInfoAttribute");
		if (customAttribute == null)
		{
			modWorldConfig = null;
			return null;
		}
		string name = customAttribute.ConstructorArguments[0].Value as string;
		string modID = customAttribute.ConstructorArguments[1].Value as string;
		ModInfoAttribute modInfoAttribute = new ModInfoAttribute(name, modID);
		foreach (Mono.Cecil.CustomAttributeNamedArgument item in customAttribute.Properties.Where((Mono.Cecil.CustomAttributeNamedArgument p) => p.Name != "Name" && p.Name != "ModID"))
		{
			PropertyInfo property = modInfoAttribute.GetType().GetProperty(item.Name);
			if (item.Argument.Value is CustomAttributeArgument[] source)
			{
				property.SetValue(modInfoAttribute, source.Select((CustomAttributeArgument item) => item.Value as string).ToArray());
			}
			else
			{
				property.SetValue(modInfoAttribute, item.Argument.Value);
			}
		}
		List<ModDependency> dependencies = (from attribute in assemblyDefinition.CustomAttributes
			where attribute.AttributeType.Name == "ModDependencyAttribute"
			select new ModDependency((string)attribute.ConstructorArguments[0].Value, attribute.ConstructorArguments[1].Value as string)).ToList();
		return LoadModInfoFromModInfoAttribute(modInfoAttribute, dependencies, out modWorldConfig);
	}

	private ModInfo LoadModInfoFromModInfoAttribute(ModInfoAttribute modInfoAttr, List<ModDependency> dependencies, out ModWorldConfiguration modWorldConfig)
	{
		if (!Enum.TryParse<EnumAppSide>(modInfoAttr.Side, ignoreCase: true, out var result))
		{
			Logger.Warning("Cannot parse '{0}', must be either 'Client', 'Server' or 'Universal'. Defaulting to 'Universal'.", modInfoAttr.Side);
			result = EnumAppSide.Universal;
		}
		if (modInfoAttr.WorldConfig != null)
		{
			modWorldConfig = JsonConvert.DeserializeObject<ModWorldConfiguration>(modInfoAttr.WorldConfig);
		}
		else
		{
			modWorldConfig = null;
		}
		return new ModInfo(EnumModType.Code, modInfoAttr.Name, modInfoAttr.ModID, modInfoAttr.Version, modInfoAttr.Description, modInfoAttr.Authors, modInfoAttr.Contributors, modInfoAttr.Website, result, modInfoAttr.RequiredOnClient, modInfoAttr.RequiredOnServer, dependencies)
		{
			NetworkVersion = modInfoAttr.NetworkVersion,
			CoreMod = modInfoAttr.CoreMod,
			IconPath = modInfoAttr.IconPath
		};
	}

	private void CheckProperVersions()
	{
		if (!string.IsNullOrEmpty(Info.Version) && !SemVer.TryParse(Info.Version, out var result, out var error))
		{
			Logger.Warning("{0} (best guess: {1})", error, result);
		}
		foreach (ModDependency dependency in Info.Dependencies)
		{
			if (!(dependency.Version == "*") && !string.IsNullOrEmpty(dependency.Version) && !SemVer.TryParse(dependency.Version, out result, out error))
			{
				Logger.Warning("Dependency '{0}': {1} (best guess: {2})", dependency.ModID, error, result);
			}
		}
	}
}
