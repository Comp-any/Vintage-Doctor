using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace Vintagestory.Client.NoObf;

[JsonObject(MemberSerialization.OptIn)]
public class ClientSettings : SettingsBase
{
	private List<Action<string, KeyCombination>> OnKeyCombinationsUpdated = new List<Action<string, KeyCombination>>();

	public static ClientSettings Inst;

	public override string FileName => Path.Combine(GamePaths.Config, "clientsettings.json");

	public override string BkpFileName => Path.Combine(GamePaths.Config, "clientsettings.bkp");

	public override string TempFileName => Path.Combine(GamePaths.Config, "clientsettings.tmp");

	public static Dictionary<string, Vec2i> DialogPositions
	{
		get
		{
			return Inst.dialogPositions;
		}
		set
		{
			Inst.dialogPositions = value;
			Inst.OtherDirty = true;
		}
	}

	public static Dictionary<string, KeyCombination> KeyMapping
	{
		get
		{
			return Inst.keyMapping;
		}
		set
		{
			Inst.keyMapping = value;
			Inst.OtherDirty = true;
		}
	}

	public static bool SelectedBlockOutline
	{
		get
		{
			return Inst.GetBoolSetting("selectedBlockOutline");
		}
		set
		{
			Inst.Bool["selectedBlockOutline"] = value;
		}
	}

	public static bool ShowPasswordProtectedServers
	{
		get
		{
			return Inst.GetBoolSetting("showPasswordProtectedServers");
		}
		set
		{
			Inst.Bool["showPasswordProtectedServers"] = value;
		}
	}

	public static bool TestGlExtensions
	{
		get
		{
			return Inst.GetBoolSetting("testGlExtensions");
		}
		set
		{
			Inst.Bool["testGlExtensions"] = value;
		}
	}

	public static int ScreenshotExifDataMode
	{
		get
		{
			return Inst.GetIntSetting("screenshotExifDataMode");
		}
		set
		{
			Inst.Int["screenshotExifDataMode"] = value;
		}
	}

	public static bool ShowOpenForAllServers
	{
		get
		{
			return Inst.GetBoolSetting("showOpenForAllServers");
		}
		set
		{
			Inst.Bool["showOpenForAllServers"] = value;
		}
	}

	public static bool ShowWhitelistedServers
	{
		get
		{
			return Inst.GetBoolSetting("showWhitelistedServers");
		}
		set
		{
			Inst.Bool["showWhitelistedServers"] = value;
		}
	}

	public static bool ShowModdedServers
	{
		get
		{
			return Inst.GetBoolSetting("showModdedServers");
		}
		set
		{
			Inst.Bool["showModdedServers"] = value;
		}
	}

	public static bool ShowMoreGfxOptions
	{
		get
		{
			return Inst.GetBoolSetting("showMoreGfxOptions");
		}
		set
		{
			Inst.Bool["showMoreGfxOptions"] = value;
		}
	}

	public static bool AllowSSBOs
	{
		get
		{
			return Inst.GetBoolSetting("allowSSBOs");
		}
		set
		{
			Inst.Bool["allowSSBOs"] = value;
		}
	}

	public static bool DynamicColorGrading
	{
		get
		{
			return Inst.GetBoolSetting("dynamicColorGrading");
		}
		set
		{
			Inst.Bool["dynamicColorGrading"] = value;
		}
	}

	public static bool Occlusionculling
	{
		get
		{
			return Inst.GetBoolSetting("occlusionculling");
		}
		set
		{
			Inst.Bool["occlusionculling"] = value;
		}
	}

	public static bool GlDebugMode
	{
		get
		{
			return Inst.GetBoolSetting("glDebugMode");
		}
		set
		{
			Inst.Bool["glDebugMode"] = value;
		}
	}

	public static bool GlErrorChecking
	{
		get
		{
			return Inst.GetBoolSetting("glErrorChecking");
		}
		set
		{
			Inst.Bool["glErrorChecking"] = value;
		}
	}

	public static bool MultipleInstances
	{
		get
		{
			return Inst.GetBoolSetting("multipleInstances");
		}
		set
		{
			Inst.Bool["multipleInstances"] = value;
		}
	}

	public static bool StartupErrorDialog
	{
		get
		{
			return Inst.GetBoolSetting("startupErrorDialog");
		}
		set
		{
			Inst.Bool["startupErrorDialog"] = value;
		}
	}

	public static bool HighQualityAnimations
	{
		get
		{
			return Inst.GetBoolSetting("highQualityAnimations");
		}
		set
		{
			Inst.Bool["highQualityAnimations"] = value;
		}
	}

	public static bool ToggleSprint
	{
		get
		{
			return Inst.GetBoolSetting("toggleSprint");
		}
		set
		{
			Inst.Bool["toggleSprint"] = value;
		}
	}

	public static bool SeparateCtrl
	{
		get
		{
			return Inst.GetBoolSetting("separateCtrlKeyForMouse");
		}
		set
		{
			Inst.Bool["separateCtrlKeyForMouse"] = value;
		}
	}

	public static int WebRequestTimeout
	{
		get
		{
			return Inst.GetIntSetting("webRequestTimeout");
		}
		set
		{
			Inst.Int["webRequestTimeout"] = value;
		}
	}

	public static int ArchiveLogFileCount
	{
		get
		{
			return Inst.GetIntSetting("archiveLogFileCount");
		}
		set
		{
			Inst.Int["archiveLogFileCount"] = value;
		}
	}

	public static int ArchiveLogFileMaxSizeMb
	{
		get
		{
			return Inst.GetIntSetting("archiveLogFileMaxSizeMb");
		}
		set
		{
			Inst.Int["archiveLogFileMaxSizeMb"] = value;
		}
	}

	public static int OptimizeRamMode
	{
		get
		{
			return Inst.GetIntSetting("optimizeRamMode");
		}
		set
		{
			Inst.Int["optimizeRamMode"] = value;
		}
	}

	public static int LeftDialogMargin
	{
		get
		{
			return Inst.GetIntSetting("leftDialogMargin");
		}
		set
		{
			Inst.Int["leftDialogMargin"] = value;
		}
	}

	public static int RightDialogMargin
	{
		get
		{
			return Inst.GetIntSetting("rightDialogMargin");
		}
		set
		{
			Inst.Int["rightDialogMargin"] = value;
		}
	}

	public static bool ShowSurvivalHelpDialog
	{
		get
		{
			return Inst.GetBoolSetting("showSurvivalHelpDialog");
		}
		set
		{
			Inst.Bool["showSurvivalHelpDialog"] = value;
		}
	}

	public static bool ShowCreativeHelpDialog
	{
		get
		{
			return Inst.GetBoolSetting("showCreativeHelpDialog");
		}
		set
		{
			Inst.Bool["showCreativeHelpDialog"] = value;
		}
	}

	public static bool ViewBobbing
	{
		get
		{
			return Inst.GetBoolSetting("viewBobbing");
		}
		set
		{
			Inst.Bool["viewBobbing"] = value;
		}
	}

	public static bool ChatDialogVisible
	{
		get
		{
			return Inst.GetBoolSetting("chatdialogvisible");
		}
		set
		{
			Inst.Bool["chatdialogvisible"] = value;
		}
	}

	public static string UserEmail
	{
		get
		{
			return Inst.GetStringSetting("useremail");
		}
		set
		{
			Inst.String["useremail"] = value;
		}
	}

	public static string MpToken
	{
		get
		{
			return Inst.GetStringSetting("mptoken");
		}
		set
		{
			Inst.String["mptoken"] = value;
		}
	}

	public static bool HasGameServer
	{
		get
		{
			return Inst.GetBoolSetting("hasGameServer");
		}
		set
		{
			Inst.Bool["hasGameServer"] = value;
		}
	}

	public static string Sessionkey
	{
		get
		{
			return Inst.GetStringSetting("sessionkey");
		}
		set
		{
			Inst.String["sessionkey"] = value;
		}
	}

	public static string SessionSignature
	{
		get
		{
			return Inst.GetStringSetting("sessionsignature");
		}
		set
		{
			Inst.String["sessionsignature"] = value;
		}
	}

	public static string MasterserverUrl
	{
		get
		{
			return Inst.GetStringSetting("masterserverUrl");
		}
		set
		{
			Inst.String["masterserverUrl"] = value;
		}
	}

	public static string ModDbUrl
	{
		get
		{
			return Inst.GetStringSetting("modDbUrl");
		}
		set
		{
			Inst.String["modDbUrl"] = value;
		}
	}

	public static string PlayerUID
	{
		get
		{
			return Inst.GetStringSetting("playeruid");
		}
		set
		{
			Inst.String["playeruid"] = value;
		}
	}

	public static string PlayerName
	{
		get
		{
			return Inst.GetStringSetting("playername");
		}
		set
		{
			Inst.String["playername"] = value;
		}
	}

	public static string Entitlements
	{
		get
		{
			return Inst.GetStringSetting("entitlements");
		}
		set
		{
			Inst.String["entitlements"] = value;
		}
	}

	public static string SettingsVersion
	{
		get
		{
			return Inst.GetStringSetting("settingsVersion");
		}
		set
		{
			Inst.String["settingsVersion"] = value;
		}
	}

	public static float GUIScale
	{
		get
		{
			return Inst.GetFloatSetting("guiScale");
		}
		set
		{
			Inst.Float["guiScale"] = value;
		}
	}

	public static float SwimmingMouseSmoothing
	{
		get
		{
			return Inst.GetFloatSetting("swimmingMouseSmoothing");
		}
		set
		{
			Inst.Float["swimmingMouseSmoothing"] = value;
		}
	}

	public static float FontSize
	{
		get
		{
			return Inst.GetFloatSetting("fontSize");
		}
		set
		{
			Inst.Float["fontSize"] = value;
		}
	}

	public static float LodBias
	{
		get
		{
			return Inst.GetFloatSetting("lodBias");
		}
		set
		{
			Inst.Float["lodBias"] = value;
		}
	}

	public static float LodBiasFar
	{
		get
		{
			return Inst.GetFloatSetting("lodBiasFar");
		}
		set
		{
			Inst.Float["lodBiasFar"] = value;
		}
	}

	public static bool SmoothShadows
	{
		get
		{
			return Inst.GetBoolSetting("smoothShadows");
		}
		set
		{
			Inst.Bool["smoothShadows"] = value;
		}
	}

	public static int SSAOQuality
	{
		get
		{
			return Inst.GetIntSetting("ssaoQuality");
		}
		set
		{
			Inst.Int["ssaoquality"] = value;
		}
	}

	public static bool FlipScreenshot
	{
		get
		{
			return Inst.GetBoolSetting("flipScreenshot");
		}
		set
		{
			Inst.Bool["flipScreenshot"] = value;
		}
	}

	public static bool DeveloperMode
	{
		get
		{
			return Inst.GetBoolSetting("developerMode");
		}
		set
		{
			Inst.Bool["developerMode"] = value;
		}
	}

	public static int ViewDistance
	{
		get
		{
			return Inst.GetIntSetting("viewDistance");
		}
		set
		{
			Inst.Int["viewDistance"] = value;
		}
	}

	public static bool FXAA
	{
		get
		{
			return Inst.GetBoolSetting("fxaa");
		}
		set
		{
			Inst.Bool["fxaa"] = value;
		}
	}

	public static bool RenderMetaBlocks
	{
		get
		{
			return Inst.GetBoolSetting("renderMetaBlocks");
		}
		set
		{
			Inst.Bool["renderMetaBlocks"] = value;
		}
	}

	public static int ChunkVerticesUploadRateLimiter
	{
		get
		{
			return Inst.GetIntSetting("chunkVerticesUploadRateLimiter");
		}
		set
		{
			Inst.Int["chunkVerticesUploadRateLimiter"] = value;
		}
	}

	public static float SSAA
	{
		get
		{
			return Inst.GetFloatSetting("ssaa");
		}
		set
		{
			Inst.Float["ssaa"] = value;
			Inst.isnewfile = false;
		}
	}

	public static float MegaScreenshotSizeMul
	{
		get
		{
			return Inst.GetFloatSetting("megaScreenshotSizeMul");
		}
		set
		{
			Inst.Float["megaScreenshotSizeMul"] = value;
		}
	}

	public static bool WavingFoliage
	{
		get
		{
			return Inst.GetBoolSetting("wavingStuff");
		}
		set
		{
			Inst.Bool["wavingStuff"] = value;
		}
	}

	public static bool LiquidFoamAndShinyEffect
	{
		get
		{
			return Inst.GetBoolSetting("liquidFoamAndShinyEffect");
		}
		set
		{
			Inst.Bool["liquidFoamAndShinyEffect"] = value;
		}
	}

	public static bool PauseGameOnLostFocus
	{
		get
		{
			return Inst.GetBoolSetting("pauseGameOnLostFocus");
		}
		set
		{
			Inst.Bool["pauseGameOnLostFocus"] = value;
		}
	}

	public static bool Bloom
	{
		get
		{
			return Inst.GetBoolSetting("bloom");
		}
		set
		{
			Inst.Bool["bloom"] = value;
		}
	}

	public static int GraphicsPresetId
	{
		get
		{
			return Inst.GetIntSetting("graphicsPresetId");
		}
		set
		{
			Inst.Int["graphicsPresetId"] = value;
		}
	}

	public static int GameWindowMode
	{
		get
		{
			return Inst.GetIntSetting("gameWindowMode");
		}
		set
		{
			Inst.Int["gameWindowMode"] = value;
		}
	}

	public static int MasterSoundLevel
	{
		get
		{
			return Inst.GetIntSetting("masterSoundLevel");
		}
		set
		{
			Inst.Int["masterSoundLevel"] = value;
		}
	}

	public static int SoundLevel
	{
		get
		{
			return Inst.GetIntSetting("soundLevel");
		}
		set
		{
			Inst.Int["soundLevel"] = value;
		}
	}

	public static int EntitySoundLevel
	{
		get
		{
			return Inst.GetIntSetting("entitySoundLevel");
		}
		set
		{
			Inst.Int["entitySoundLevel"] = value;
		}
	}

	public static int AmbientSoundLevel
	{
		get
		{
			return Inst.GetIntSetting("ambientSoundLevel");
		}
		set
		{
			Inst.Int["ambientSoundLevel"] = value;
		}
	}

	public static int WeatherSoundLevel
	{
		get
		{
			return Inst.GetIntSetting("weatherSoundLevel");
		}
		set
		{
			Inst.Int["weatherSoundLevel"] = value;
		}
	}

	public static int MusicLevel
	{
		get
		{
			return Inst.GetIntSetting("musicLevel");
		}
		set
		{
			Inst.Int["musicLevel"] = value;
		}
	}

	public static int MusicFrequency
	{
		get
		{
			return Inst.GetIntSetting("musicFrequency");
		}
		set
		{
			Inst.Int["musicFrequency"] = value;
		}
	}

	public static string Language
	{
		get
		{
			return Inst.GetStringSetting("language");
		}
		set
		{
			Inst.String["language"] = value;
		}
	}

	public static string DefaultFontName
	{
		get
		{
			return Inst.GetStringSetting("defaultFontName");
		}
		set
		{
			Inst.String["defaultFontName"] = value;
		}
	}

	public static string DecorativeFontName
	{
		get
		{
			return Inst.GetStringSetting("decorativeFontName");
		}
		set
		{
			Inst.String["decorativeFontName"] = value;
		}
	}

	public static bool UseServerTextures
	{
		get
		{
			return Inst.GetBoolSetting("useServerTextures");
		}
		set
		{
			Inst.Bool["useServerTextures"] = value;
		}
	}

	public static int ScreenWidth
	{
		get
		{
			return Inst.GetIntSetting("screenWidth");
		}
		set
		{
			Inst.Int["screenWidth"] = value;
		}
	}

	public static int ScreenHeight
	{
		get
		{
			return Inst.GetIntSetting("screenHeight");
		}
		set
		{
			Inst.Int["screenHeight"] = value;
		}
	}

	public static int WeirdMacOSMouseYOffset
	{
		get
		{
			return Inst.GetIntSetting("weirdMacOSMouseYOffset");
		}
		set
		{
			Inst.Int["weirdMacOSMouseYOffset"] = value;
		}
	}

	public static float BlockAtlasSubPixelPadding
	{
		get
		{
			return Inst.GetFloatSetting("blockAtlasSubPixelPadding");
		}
		set
		{
			Inst.Float["blockAtlasSubPixelPadding"] = value;
		}
	}

	public static float ItemAtlasSubPixelPadding
	{
		get
		{
			return Inst.GetFloatSetting("itemAtlasSubPixelPadding");
		}
		set
		{
			Inst.Float["itemAtlasSubPixelPadding"] = value;
		}
	}

	public static int MipMapLevel
	{
		get
		{
			return Inst.GetIntSetting("mipmapLevel");
		}
		set
		{
			Inst.Int["mipmapLevel"] = value;
		}
	}

	public static int MaxQuadParticles
	{
		get
		{
			return Inst.GetIntSetting("maxQuadParticles");
		}
		set
		{
			Inst.Int["maxQuadParticles"] = value;
		}
	}

	public static int MaxCubeParticles
	{
		get
		{
			return Inst.GetIntSetting("maxCubeParticles");
		}
		set
		{
			Inst.Int["maxCubeParticles"] = value;
		}
	}

	public static int MaxAsyncQuadParticles
	{
		get
		{
			return Inst.GetIntSetting("maxAsyncQuadParticles");
		}
		set
		{
			Inst.Int["maxAsyncQuadParticles"] = value;
		}
	}

	public static int MaxAsyncCubeParticles
	{
		get
		{
			return Inst.GetIntSetting("maxAsyncCubeParticles");
		}
		set
		{
			Inst.Int["maxAsyncCubeParticles"] = value;
		}
	}

	public static int MouseSensivity
	{
		get
		{
			return Inst.GetIntSetting("mouseSensivity");
		}
		set
		{
			Inst.Int["mouseSensivity"] = value;
		}
	}

	public static int MouseSmoothing
	{
		get
		{
			return Inst.GetIntSetting("mouseSmoothing");
		}
		set
		{
			Inst.Int["mouseSmoothing"] = value;
		}
	}

	public static int VsyncMode
	{
		get
		{
			return Inst.GetIntSetting("vsyncMode");
		}
		set
		{
			Inst.Int["vsyncMode"] = value;
		}
	}

	public static int ModelDataPoolMaxVertexSize
	{
		get
		{
			return Inst.GetIntSetting("modelDataPoolMaxVertexSize");
		}
		set
		{
			Inst.Int["modelDataPoolMaxVertexSize"] = value;
		}
	}

	public static int ModelDataPoolMaxIndexSize
	{
		get
		{
			return Inst.GetIntSetting("modelDataPoolMaxIndexSize");
		}
		set
		{
			Inst.Int["modelDataPoolMaxIndexSize"] = value;
		}
	}

	public static int ModelDataPoolMaxParts
	{
		get
		{
			return Inst.GetIntSetting("modelDataPoolMaxParts");
		}
		set
		{
			Inst.Int["modelDataPoolMaxParts"] = value;
		}
	}

	public static int FieldOfView
	{
		get
		{
			return Inst.GetIntSetting("fieldOfView");
		}
		set
		{
			Inst.Int["fieldOfView"] = value;
		}
	}

	public static int MaxTextureAtlasWidth
	{
		get
		{
			return Inst.GetIntSetting("maxTextureAtlasWidth");
		}
		set
		{
			Inst.Int["maxTextureAtlasWidth"] = value;
		}
	}

	public static int MaxTextureAtlasHeight
	{
		get
		{
			return Inst.GetIntSetting("maxTextureAtlasHeight");
		}
		set
		{
			Inst.Int["maxTextureAtlasHeight"] = value;
		}
	}

	public static bool SkipNvidiaProfileCheck
	{
		get
		{
			return Inst.GetBoolSetting("skipNvidiaProfileCheck");
		}
		set
		{
			Inst.Bool["skipNvidiaProfileCheck"] = value;
		}
	}

	public static float AmbientBloomLevel
	{
		get
		{
			return Inst.GetFloatSetting("ambientBloomLevel");
		}
		set
		{
			Inst.Float["ambientBloomLevel"] = value;
		}
	}

	public static float ExtraContrastLevel
	{
		get
		{
			return Inst.GetFloatSetting("extraContrastLevel");
		}
		set
		{
			Inst.Float["extraContrastLevel"] = value;
		}
	}

	public static int GodRayQuality
	{
		get
		{
			return Inst.GetIntSetting("godRays");
		}
		set
		{
			Inst.Int["godRays"] = value;
		}
	}

	public static float Minbrightness
	{
		get
		{
			return Inst.GetFloatSetting("minbrightness");
		}
		set
		{
			Inst.Float["minbrightness"] = value;
		}
	}

	public static int RecordingBufferSize
	{
		get
		{
			return Inst.GetIntSetting("recordingBufferSize");
		}
		set
		{
			Inst.Int["recordingBufferSize"] = value;
		}
	}

	public static bool UseHRTFAudio
	{
		get
		{
			return Inst.GetBoolSetting("useHRTFaudio");
		}
		set
		{
			Inst.Bool["useHRTFaudio"] = value;
		}
	}

	public static bool AllowSettingHRTFAudio
	{
		get
		{
			return Inst.GetBoolSetting("allowSettingHRTFaudio");
		}
		set
		{
			Inst.Bool["allowSettingHRTFaudio"] = value;
		}
	}

	public static bool Force48kHzHRTFAudio
	{
		get
		{
			return Inst.GetBoolSetting("force48khzHRTFaudio");
		}
		set
		{
			Inst.Bool["force48khzHRTFaudio"] = value;
		}
	}

	public static bool RenderParticles
	{
		get
		{
			return Inst.GetBoolSetting("renderParticles");
		}
		set
		{
			Inst.Bool["renderParticles"] = value;
		}
	}

	public static bool AmbientParticles
	{
		get
		{
			return Inst.GetBoolSetting("ambientParticles");
		}
		set
		{
			Inst.Bool["ambientParticles"] = value;
		}
	}

	public static int CloudRenderMode
	{
		get
		{
			return Inst.GetIntSetting("cloudRenderMode");
		}
		set
		{
			Inst.Int["cloudRenderMode"] = value;
		}
	}

	public static bool TransparentRenderPass
	{
		get
		{
			return Inst.GetBoolSetting("transparentRenderPass");
		}
		set
		{
			Inst.Bool["transparentRenderPass"] = value;
		}
	}

	public static float GammaLevel
	{
		get
		{
			return Inst.GetFloatSetting("gammaLevel");
		}
		set
		{
			Inst.Float["gammaLevel"] = value;
		}
	}

	public static float ExtraGammaLevel
	{
		get
		{
			return Inst.GetFloatSetting("extraGammaLevel");
		}
		set
		{
			Inst.Float["extraGammaLevel"] = value;
		}
	}

	public static float BrightnessLevel
	{
		get
		{
			return Inst.GetFloatSetting("brightnessLevel");
		}
		set
		{
			Inst.Float["brightnessLevel"] = value;
		}
	}

	public static float SepiaLevel
	{
		get
		{
			return Inst.GetFloatSetting("sepiaLevel");
		}
		set
		{
			Inst.Float["sepiaLevel"] = value;
		}
	}

	public static float CameraShakeStrength
	{
		get
		{
			return Inst.GetFloatSetting("cameraShakeStrength");
		}
		set
		{
			Inst.Float["cameraShakeStrength"] = value;
		}
	}

	public static float Wireframethickness
	{
		get
		{
			return Inst.GetFloatSetting("wireframethickness");
		}
		set
		{
			Inst.Float["wireframethickness"] = value;
		}
	}

	public static int guiColorsPreset
	{
		get
		{
			return Inst.GetIntSetting("guiColorsPreset");
		}
		set
		{
			Inst.Int["guiColorsPreset"] = value;
		}
	}

	public static float InstabilityWavingStrength
	{
		get
		{
			return Inst.GetFloatSetting("instabilityWavingStrength");
		}
		set
		{
			Inst.Float["instabilityWavingStrength"] = value;
		}
	}

	public static List<string> ModPaths
	{
		get
		{
			return Inst.GetStringListSetting("modPaths");
		}
		set
		{
			Inst.Strings["modPaths"] = value;
		}
	}

	public static List<string> DisabledMods
	{
		get
		{
			return Inst.GetStringListSetting("disabledMods");
		}
		set
		{
			Inst.Strings["disabledMods"] = value;
		}
	}

	public static bool ExtendedDebugInfo
	{
		get
		{
			return Inst.GetBoolSetting("extendedDebugInfo");
		}
		set
		{
			Inst.Bool["extendedDebugInfo"] = value;
		}
	}

	public static bool ScaleScreenshot
	{
		get
		{
			return Inst.GetBoolSetting("scaleScreenshot");
		}
		set
		{
			Inst.Bool["scaleScreenshot"] = value;
		}
	}

	public static float RecordingFrameRate
	{
		get
		{
			return Inst.GetFloatSetting("recordingFrameRate");
		}
		set
		{
			Inst.Float["recordingFrameRate"] = value;
		}
	}

	public static float GameTickFrameRate
	{
		get
		{
			return Inst.GetFloatSetting("gameTickFrameRate");
		}
		set
		{
			Inst.Float["gameTickFrameRate"] = value;
		}
	}

	public static string RecordingCodec
	{
		get
		{
			return Inst.GetStringSetting("recordingCodec");
		}
		set
		{
			Inst.String["recordingCodec"] = value;
		}
	}

	public static int ChatWindowWidth
	{
		get
		{
			return Inst.GetIntSetting("chatWindowWidth");
		}
		set
		{
			Inst.Int["chatWindowWidth"] = value;
		}
	}

	public static int ChatWindowHeight
	{
		get
		{
			return Inst.GetIntSetting("chatWindowHeight");
		}
		set
		{
			Inst.Int["chatWindowHeight"] = value;
		}
	}

	public static int MaxFPS
	{
		get
		{
			return Inst.GetIntSetting("maxFps");
		}
		set
		{
			Inst.Int["maxFps"] = value;
		}
	}

	public static bool ShowEntityDebugInfo
	{
		get
		{
			return Inst.GetBoolSetting("showEntityDebugInfo");
		}
		set
		{
			Inst.Bool["showEntityDebugInfo"] = value;
		}
	}

	public static bool ShowBlockInfoHud
	{
		get
		{
			return Inst.GetBoolSetting("showBlockInfoHud");
		}
		set
		{
			Inst.Bool["showBlockInfoHud"] = value;
		}
	}

	public static bool ShowBlockInteractionHelp
	{
		get
		{
			return Inst.GetBoolSetting("showBlockInteractionHelp");
		}
		set
		{
			Inst.Bool["showBlockInteractionHelp"] = value;
		}
	}

	public static bool ShowCoordinateHud
	{
		get
		{
			return Inst.GetBoolSetting("showCoordinateHud");
		}
		set
		{
			Inst.Bool["showCoordinateHud"] = value;
		}
	}

	public static int ShadowMapQuality
	{
		get
		{
			return Inst.GetIntSetting("shadowMapQuality");
		}
		set
		{
			Inst.Int["shadowMapQuality"] = value;
		}
	}

	public static int ParticleLevel
	{
		get
		{
			return Inst.GetIntSetting("particleLevel");
		}
		set
		{
			Inst.Int["particleLevel"] = value;
		}
	}

	public static int MaxDynamicLights
	{
		get
		{
			return Inst.GetIntSetting("maxDynamicLights");
		}
		set
		{
			Inst.Int["maxDynamicLights"] = value;
		}
	}

	public static float MouseWheelSensivity
	{
		get
		{
			return Inst.GetFloatSetting("mouseWheelSensivity");
		}
		set
		{
			Inst.Float["mouseWheelSensivity"] = value;
		}
	}

	public static string VideoFileTarget
	{
		get
		{
			return Inst.GetStringSetting("videofiletarget");
		}
		set
		{
			Inst.String["videofiletarget"] = value;
		}
	}

	public static string GlContextVersion
	{
		get
		{
			return Inst.GetStringSetting("glContextVersion");
		}
		set
		{
			Inst.String["glContextVersion"] = value;
		}
	}

	public static string AudioDevice
	{
		get
		{
			return Inst.GetStringSetting("audioDevice");
		}
		set
		{
			Inst.String["audioDevice"] = value;
		}
	}

	public static bool DirectMouseMode
	{
		get
		{
			return Inst.GetBoolSetting("directMouseMode");
		}
		set
		{
			Inst.Bool["directMouseMode"] = value;
		}
	}

	public static bool InvertMouseYAxis
	{
		get
		{
			return Inst.GetBoolSetting("invertMouseYAxis");
		}
		set
		{
			Inst.Bool["invertMouseYAxis"] = value;
		}
	}

	public static bool ImmersiveMouseMode
	{
		get
		{
			return Inst.GetBoolSetting("immersiveMouseMode");
		}
		set
		{
			Inst.Bool["immersiveMouseMode"] = value;
		}
	}

	public static bool ImmersiveFpMode
	{
		get
		{
			return Inst.GetBoolSetting("immersiveFpMode");
		}
		set
		{
			Inst.Bool["immersiveFpMode"] = value;
		}
	}

	public static bool AutoChat
	{
		get
		{
			return Inst.GetBoolSetting("autoChat");
		}
		set
		{
			Inst.Bool["autoChat"] = value;
		}
	}

	public static bool AutoChatOpenSelected
	{
		get
		{
			return Inst.GetBoolSetting("autoChatOpenSelected");
		}
		set
		{
			Inst.Bool["autoChatOpenSelected"] = value;
		}
	}

	public static int ItemCollectMode
	{
		get
		{
			return Inst.GetIntSetting("itemCollectMode");
		}
		set
		{
			Inst.Int["itemCollectMode"] = value;
		}
	}

	public static int WindowBorder
	{
		get
		{
			return Inst.GetIntSetting("windowBorder");
		}
		set
		{
			Inst.Int["windowBorder"] = value;
		}
	}

	public static bool FocusWindowOnModInstallRequest
	{
		get
		{
			return Inst.GetBoolSetting("focusWindowOnModInstallRequest");
		}
		set
		{
			Inst.Bool["focusWindowOnModInstallRequest"] = value;
		}
	}

	public static bool OffThreadMipMapCreation
	{
		get
		{
			return Inst.GetBoolSetting("offThreadMipMaps");
		}
		set
		{
			Inst.Bool["offThreadMipMaps"] = value;
		}
	}

	public static float FpHandsYOffset
	{
		get
		{
			return Inst.GetFloatSetting("fpHandsYOffset");
		}
		set
		{
			Inst.Float["fpHandsYOffset"] = value;
		}
	}

	public static int FpHandsFoV
	{
		get
		{
			return Inst.GetIntSetting("fpHandsFoV");
		}
		set
		{
			Inst.Int["fpHandsFoV"] = value;
		}
	}

	public static bool DisableModSafetyCheck
	{
		get
		{
			return Inst.GetBoolSetting("disableModSafetyCheck");
		}
		set
		{
			Inst.Bool["disableModSafetyCheck"] = value;
		}
	}

	public static bool PromptForCommandLinks
	{
		get
		{
			return Inst.GetBoolSetting("promptForCommandLinks");
		}
		set
		{
			Inst.Bool["promptForCommandLinks"] = value;
		}
	}

	public static bool ForceUdpOverTcp
	{
		get
		{
			return Inst.GetBoolSetting("forceUdpOverTcp");
		}
		set
		{
			Inst.Bool["forceUdpOverTcp"] = value;
		}
	}

	public static int AuctionSearchbarThreshold
	{
		get
		{
			return Inst.GetIntSetting("auctionSearchbarThreshold");
		}
		set
		{
			Inst.Int["auctionSearchbarThreshold"] = value;
		}
	}

	public static bool IsNewSettingsFile => Inst.isnewfile;

	static ClientSettings()
	{
		Inst = new ClientSettings();
		try
		{
			Inst.Load();
		}
		catch (Exception e)
		{
			ScreenManager.Platform.Logger.Error("Couldn't load client settings, probably problems with parsing json. Will use default values. The error was:");
			ScreenManager.Platform.Logger.Error(e);
			Inst.LoadDefaultValues();
		}
		RuntimeEnv.GUIScale = GUIScale;
		if (Inst.isnewfile && File.Exists("default.lang"))
		{
			Language = File.ReadAllText("default.lang").Trim();
		}
	}

	public void SetDialogPosition(string key, Vec2i pos)
	{
		Inst.dialogPositions[key] = pos;
		Inst.OtherDirty = true;
	}

	public Vec2i GetDialogPosition(string key)
	{
		Inst.dialogPositions.TryGetValue(key, out var value);
		return value;
	}

	public void SetKeyMapping(string key, KeyCombination value)
	{
		keyMapping[key] = value;
		Inst.OtherDirty = true;
		foreach (Action<string, KeyCombination> item in OnKeyCombinationsUpdated)
		{
			item(key, value);
		}
	}

	public KeyCombination GetKeyMapping(string key)
	{
		keyMapping.TryGetValue(key, out var value);
		return value;
	}

	public void AddKeyCombinationUpdatedWatcher(Action<string, KeyCombination> handler)
	{
		OnKeyCombinationsUpdated.Add(handler);
	}

	public override void ClearWatchers()
	{
		base.ClearWatchers();
		OnKeyCombinationsUpdated.Clear();
	}

	private ClientSettings()
	{
		keyMapping = new Dictionary<string, KeyCombination>();
		dialogPositions = new Dictionary<string, Vec2i>();
	}

	public override void LoadDefaultValues()
	{
		stringSettings["settingsVersion"] = "1.16";
		floatSettings["guiScale"] = 1f;
		floatSettings["fontSize"] = 1f;
		floatSettings["lodBias"] = 0.33f;
		floatSettings["lodBiasFar"] = 0.67f;
		floatSettings["blockAtlasSubPixelPadding"] = 0.01f;
		floatSettings["itemAtlasSubPixelPadding"] = 0f;
		floatSettings["gammaLevel"] = 2.2f;
		floatSettings["extraGammaLevel"] = 1f;
		floatSettings["brightnessLevel"] = 1f;
		floatSettings["sepiaLevel"] = 0.2f;
		floatSettings["cameraShakeStrength"] = 1f;
		floatSettings["wireframethickness"] = 1f;
		floatSettings["previewTransparency"] = 0.3f;
		floatSettings["fpHandsYOffset"] = 0f;
		floatSettings["swimmingMouseSmoothing"] = 0.9f;
		intSettings["fpHandsFoV"] = 75;
		intSettings["cloudRenderMode"] = 1;
		intSettings["mipmapLevel"] = 3;
		intSettings["musicFrequency"] = 2;
		intSettings["graphicsPresetId"] = 6;
		intSettings["webRequestTimeout"] = 10;
		intSettings["maxAnimatedElements"] = 230;
		intSettings["archiveLogFileCount"] = 5;
		intSettings["archiveLogFileMaxSizeMb"] = 1024;
		boolSettings["selectedBlockOutline"] = true;
		boolSettings["showMoreGfxOptions"] = false;
		boolSettings["dynamicColorGrading"] = true;
		boolSettings["multipleInstances"] = false;
		boolSettings["showSurvivalHelpDialog"] = true;
		boolSettings["showCreativeHelpDialog"] = true;
		boolSettings["smoothShadows"] = true;
		boolSettings["flipScreenshot"] = true;
		boolSettings["renderParticles"] = true;
		boolSettings["transparentRenderPass"] = true;
		boolSettings["extendedDebugInfo"] = false;
		boolSettings["showentitydebuginfo"] = false;
		boolSettings["showBlockInfoHud"] = true;
		boolSettings["showBlockInteractionHelp"] = true;
		boolSettings["showCoordinateHud"] = false;
		intSettings["shadowMapQuality"] = 0;
		boolSettings["wavingStuff"] = true;
		boolSettings["renderMetaBlocks"] = false;
		boolSettings["ambientParticles"] = true;
		boolSettings["pauseGameOnLostFocus"] = false;
		boolSettings["viewBobbing"] = true;
		boolSettings["invertMouseYAxis"] = false;
		boolSettings["highQualityAnimations"] = true;
		intSettings["optimizeRamMode"] = 1;
		boolSettings["occlusionculling"] = true;
		boolSettings["focusWindowOnModInstallRequest"] = true;
		boolSettings["developerMode"] = false;
		boolSettings["glDebugMode"] = false;
		boolSettings["showWhitelistedServers"] = true;
		boolSettings["showModdedServers"] = true;
		boolSettings["showPasswordProtectedServers"] = true;
		boolSettings["showOpenForAllServers"] = true;
		boolSettings["liquidFoamAndShinyEffect"] = true;
		boolSettings["testGlExtensions"] = true;
		boolSettings["offThreadMipMaps"] = false;
		intSettings["ssaoQuality"] = 0;
		intSettings["viewDistance"] = 256;
		intSettings["fieldOfView"] = 70;
		intSettings["maxFps"] = 241;
		intSettings["particleLevel"] = 100;
		intSettings["maxDynamicLights"] = 10;
		IntSettings["weirdMacOSMouseYOffset"] = 5;
		IntSettings["screenshotExifDataMode"] = 0;
		IntSettings["chunkVerticesUploadRateLimiter"] = 3;
		intSettings["masterSoundLevel"] = 100;
		intSettings["soundLevel"] = 100;
		intSettings["entitySoundLevel"] = 100;
		intSettings["ambientSoundLevel"] = 100;
		intSettings["weatherSoundLevel"] = 100;
		intSettings["musicLevel"] = 20;
		intSettings["screenWidth"] = 1024;
		intSettings["screenHeight"] = 768;
		intSettings["gameWindowMode"] = 1;
		intSettings["rightDialogMargin"] = 0;
		intSettings["leftDialogMargin"] = 0;
		stringSettings["language"] = "en";
		stringSettings["defaultFontName"] = "sans-serif";
		stringSettings["decorativeFontName"] = "Lora";
		intSettings["maxQuadParticles"] = 8000;
		intSettings["maxCubeParticles"] = 4000;
		intSettings["maxAsyncQuadParticles"] = 80000;
		intSettings["maxAsyncCubeParticles"] = 80000;
		intSettings["itemCollectMode"] = 0;
		boolSettings["toggleSprint"] = false;
		boolSettings["separateCtrlKeyForMouse"] = false;
		boolSettings["allowSettingHRTFaudio"] = true;
		boolSettings["useHRTFaudio"] = false;
		boolSettings["force48khzHRTFaudio"] = true;
		intSettings["mouseSmoothing"] = 30;
		intSettings["mouseSensivity"] = 50;
		floatSettings["mouseWheelSensivity"] = ((RuntimeEnv.OS != OS.Mac) ? 1 : 10);
		intSettings["modelDataPoolMaxVertexSize"] = 500000;
		intSettings["modelDataPoolMaxIndexSize"] = 750000;
		intSettings["modelDataPoolMaxParts"] = 1500;
		intSettings["maxTextureAtlasWidth"] = 4096;
		intSettings["maxTextureAtlasHeight"] = 2048;
		floatSettings["ambientBloomLevel"] = 0.2f;
		floatSettings["extraContrastLevel"] = 0f;
		intSettings["godRays"] = 0;
		intSettings["chatWindowWidth"] = 700;
		intSettings["chatWindowHeight"] = 200;
		intSettings["recordingBufferSize"] = 60;
		floatSettings["recordingFrameRate"] = 30f;
		floatSettings["gameTickFrameRate"] = -1f;
		floatSettings["instabilityWavingStrength"] = 1f;
		stringSettings["recordingCodec"] = "rawv";
		intSettings["vsyncMode"] = 1;
		intSettings["windowBorder"] = 0;
		boolSettings["fxaa"] = true;
		boolSettings["autoChat"] = true;
		boolSettings["autoChatOpenSelected"] = true;
		floatSettings["ssaa"] = 1f;
		floatSettings["megaScreenshotSizeMul"] = 2f;
		floatSettings["minbrightness"] = 0f;
		boolSettings["bloom"] = true;
		boolSettings["skipNvidiaProfileCheck"] = false;
		boolSettings["scaleScreenshot"] = false;
		boolSettings["immersiveMouseMode"] = false;
		boolSettings["startupErrorDialog"] = false;
		boolSettings["allowSSBOs"] = DefaultSSBOsSetting();
		stringSettings["glContextVersion"] = "4.3";
		stringSettings["mptoken"] = "";
		stringSettings["sessionkey"] = "";
		stringSettings["sessionsignature"] = "";
		stringSettings["useremail"] = "";
		stringSettings["entitlements"] = "";
		stringSettings["masterserverUrl"] = "https://masterserver.vintagestory.at/api/v1/servers/";
		stringSettings["modDbUrl"] = "https://mods.vintagestory.at/";
		stringListSettings["multiplayerservers"] = new List<string>();
		stringListSettings["disabledMods"] = new List<string>();
		stringListSettings["dialogPositions"] = new List<string>();
		stringListSettings["modPaths"] = new List<string>(new string[2]
		{
			"Mods",
			GamePaths.DataPathMods
		});
		stringListSettings["customPlayStyles"] = new List<string>();
		intSettings["guiColorsPreset"] = 1;
		boolSettings["disableModSafetyCheck"] = false;
		boolSettings["forceUdpOverTcp"] = false;
		Inst.Int["auctionSearchbarThreshold"] = 50;
		GraphicsPreset high = GraphicsPreset.High;
		GraphicsPresetId = high.PresetId;
		ViewDistance = high.ViewDistance;
		SmoothShadows = high.SmoothLight;
		FXAA = high.FXAA;
		SSAOQuality = high.SSAO;
		WavingFoliage = high.WavingFoliage;
		LiquidFoamAndShinyEffect = high.LiquidFoamEffect;
		Bloom = high.Bloom;
		GodRayQuality = (high.GodRays ? 1 : 0);
		ShadowMapQuality = high.ShadowMapQuality;
		ParticleLevel = high.ParticleLevel;
		MaxDynamicLights = high.DynamicLights;
		SSAA = high.Resolution;
	}

	internal override void DidDeserialize()
	{
		if (keyMapping == null)
		{
			keyMapping = new Dictionary<string, KeyCombination>();
		}
		if (dialogPositions == null)
		{
			dialogPositions = new Dictionary<string, Vec2i>();
		}
		intSettings.Remove("ambientBloomLevel");
		if (SettingsVersion == null)
		{
			GammaLevel = 2.2f;
			SettingsVersion = "1.0";
			Save();
		}
		if (SettingsVersion == "1.0")
		{
			intSettings["maxQuadParticles"] = 8000;
			intSettings["maxCubeParticles"] = 4000;
			SettingsVersion = "1.1";
			Save();
		}
		if (SettingsVersion == "1.1")
		{
			BrightnessLevel = 1f;
			ExtraGammaLevel = 1f;
			SettingsVersion = "1.2";
			Save();
		}
		if (SettingsVersion == "1.2")
		{
			MipMapLevel = 3;
			SettingsVersion = "1.3";
			Save();
		}
		if (SettingsVersion == "1.3")
		{
			ScaleScreenshot = false;
			LodBias = 0.33f;
			MegaScreenshotSizeMul = 2f;
			SettingsVersion = "1.4";
			Save();
		}
		if (SettingsVersion == "1.4")
		{
			stringSettings["modDbUrl"] = "https://mods.vintagestory.at/";
			SettingsVersion = "1.5";
			Save();
		}
		if (SettingsVersion == "1.5")
		{
			intSettings["maxTextureAtlasHeight"] = 4096;
			SettingsVersion = "1.6";
			Save();
		}
		if (SettingsVersion == "1.6" || SettingsVersion == "1.7")
		{
			SettingsVersion = "1.8";
			Save();
		}
		if (SettingsVersion == "1.8" || SettingsVersion == "1.9" || SettingsVersion == "1.10")
		{
			intSettings["maxAnimatedElements"] = 46;
			SettingsVersion = "1.11";
			Save();
		}
		if (SettingsVersion == "1.11")
		{
			if (intSettings["fpHandsFoV"] == 90)
			{
				intSettings["fpHandsFoV"] = 75;
			}
			SettingsVersion = "1.12";
			Save();
		}
		if (SettingsVersion == "1.12")
		{
			intSettings["maxAnimatedElements"] = 230;
			SettingsVersion = "1.13";
			Save();
		}
		if (SettingsVersion == "1.13")
		{
			if (stringSettings["glContextVersion"] == "3.3" && RuntimeEnv.OS != OS.Mac)
			{
				stringSettings["glContextVersion"] = "4.3";
			}
			boolSettings["allowSSBOs"] = DefaultSSBOsSetting();
			SettingsVersion = "1.14";
			if (RuntimeEnv.OS == OS.Mac)
			{
				string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify), ".config", "VintagestoryData");
				List<string> modPaths = ModPaths;
				for (int i = 0; i < modPaths.Count; i++)
				{
					if (modPaths[i].Contains(text))
					{
						modPaths[i] = modPaths[i].Replace(text, GamePaths.Config);
					}
				}
				Strings["modPaths"] = modPaths;
			}
			Save();
		}
		if (SettingsVersion == "1.14")
		{
			boolSettings["focusWindowOnModInstallRequest"] = true;
			SettingsVersion = "1.15";
			Save();
		}
		if (SettingsVersion == "1.15")
		{
			boolSettings["promptForCommandLinks"] = true;
			SettingsVersion = "1.16";
			Save();
		}
		if (!boolSettings.TryGetValue("separateCtrlKeyForMouse", out var value) || !value)
		{
			if (keyMapping.TryGetValue("sprint", out var value2) && !keyMapping.ContainsKey("ctrl"))
			{
				keyMapping["ctrl"] = value2.Clone();
			}
			if (keyMapping.TryGetValue("sneak", out var value3) && !keyMapping.ContainsKey("shift"))
			{
				keyMapping["shift"] = value3.Clone();
			}
		}
		GlobalConstants.MaxAnimatedElements = intSettings["maxAnimatedElements"];
	}

	public override bool Save(bool force = false)
	{
		bool flag = base.Save(force);
		if (!flag)
		{
			ClientPlatformAbstract platform = ScreenManager.Platform;
			if (platform == null)
			{
				return flag;
			}
			platform.Logger.Notification("Failed saving clientsettings.json, will try again in a few seconds");
		}
		return flag;
	}

	public virtual bool DefaultSSBOsSetting()
	{
		return RuntimeEnv.OS != OS.Mac;
	}
}
