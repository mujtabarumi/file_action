using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static FileAction.Core.Loc;

namespace FileAction.Desktop.Mac;

/// <summary>
/// "FileAction…" as a Quick Action in Finder's right-click menu (an Automator workflow in ~/Library/Services)
/// and a launch agent that starts the menu-bar app at login.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class FinderIntegration
{
    public const string AgentLabel = "org.fileaction.agent";

    private static string Library => Path.Combine(DesktopSettings.Home, "Library");
    private static string Workflow => Path.Combine(Library, "Services", "FileAction.workflow");
    private static string LaunchAgent => Path.Combine(Library, "LaunchAgents", AgentLabel + ".plist");

    [LibraryImport("libc")]
    private static partial uint getuid();

    public static IEnumerable<string> Apply(bool install, string command)
    {
        if (install)
        {
            Directory.CreateDirectory(Path.Combine(Workflow, "Contents"));
            File.WriteAllText(Path.Combine(Workflow, "Contents", "Info.plist"), WorkflowInfo);
            File.WriteAllText(Path.Combine(Workflow, "Contents", "document.wflow"), WorkflowDocument(command));
            Desktop.Start("/System/Library/CoreServices/pbs", "-update");
            yield return T($"Přidáno: Finder → Rychlé akce → FileAction… ({Workflow})", $"Added: Finder → Quick Actions → FileAction… ({Workflow})");

            Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgent)!);
            File.WriteAllText(LaunchAgent, LaunchAgentPlist(command));
            Desktop.Start("launchctl", "bootstrap", $"gui/{getuid()}", LaunchAgent);
            yield return T($"Přidáno: spouštění po přihlášení ({LaunchAgent})", $"Added: start at login ({LaunchAgent})");
            yield return T("Napoprvé se může Finder zeptat, zda FileAction smí Finder ovládat (kvůli zkratce ⌃⌥␣).",
                "Finder may ask once whether FileAction is allowed to control it (for the ⌃⌥␣ shortcut).");
            yield break;
        }
        if (Directory.Exists(Workflow))
        {
            Directory.Delete(Workflow, recursive: true);
            Desktop.Start("/System/Library/CoreServices/pbs", "-update");
            yield return T("Odebráno: Rychlá akce ve Finderu", "Removed: the Quick Action in Finder");
        }
        if (File.Exists(LaunchAgent))
        {
            Desktop.Start("launchctl", "bootout", $"gui/{getuid()}/{AgentLabel}");
            File.Delete(LaunchAgent);
            yield return T("Odebráno: spouštění po přihlášení", "Removed: start at login");
        }
    }

    private static string Escape(string text) => System.Security.SecurityElement.Escape(text) ?? "";

    /// <summary>A Quick Action that passes the selected files to the app on the command line.</summary>
    public static string WorkflowDocument(string command) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
        	<key>AMApplicationBuild</key>
        	<string>523</string>
        	<key>AMApplicationVersion</key>
        	<string>2.10</string>
        	<key>AMDocumentVersion</key>
        	<string>2</string>
        	<key>actions</key>
        	<array>
        		<dict>
        			<key>action</key>
        			<dict>
        				<key>AMAccepts</key>
        				<dict>
        					<key>Container</key>
        					<string>List</string>
        					<key>Optional</key>
        					<true/>
        					<key>Types</key>
        					<array>
        						<string>com.apple.cocoa.string</string>
        					</array>
        				</dict>
        				<key>AMActionVersion</key>
        				<string>2.0.3</string>
        				<key>AMApplication</key>
        				<array>
        					<string>Automator</string>
        				</array>
        				<key>AMParameterProperties</key>
        				<dict>
        					<key>COMMAND_STRING</key>
        					<dict/>
        					<key>CheckedForUserDefaultShell</key>
        					<dict/>
        					<key>inputMethod</key>
        					<dict/>
        					<key>shell</key>
        					<dict/>
        					<key>source</key>
        					<dict/>
        				</dict>
        				<key>AMProvides</key>
        				<dict>
        					<key>Container</key>
        					<string>List</string>
        					<key>Types</key>
        					<array>
        						<string>com.apple.cocoa.string</string>
        					</array>
        				</dict>
        				<key>ActionBundlePath</key>
        				<string>/System/Library/Automator/Run Shell Script.action</string>
        				<key>ActionName</key>
        				<string>Run Shell Script</string>
        				<key>ActionParameters</key>
        				<dict>
        					<key>COMMAND_STRING</key>
        					<string>"{Escape(command)}" "$@" &gt;/dev/null 2&gt;&amp;1 &amp;</string>
        					<key>CheckedForUserDefaultShell</key>
        					<true/>
        					<key>inputMethod</key>
        					<integer>1</integer>
        					<key>shell</key>
        					<string>/bin/sh</string>
        					<key>source</key>
        					<string></string>
        				</dict>
        				<key>BundleIdentifier</key>
        				<string>com.apple.RunShellScript</string>
        				<key>CFBundleVersion</key>
        				<string>2.0.3</string>
        				<key>CanShowSelectedItemsWhenRun</key>
        				<false/>
        				<key>CanShowWhenRun</key>
        				<true/>
        				<key>Category</key>
        				<array>
        					<string>AMCategoryUtilities</string>
        				</array>
        				<key>Class Name</key>
        				<string>RunShellScriptAction</string>
        				<key>InputUUID</key>
        				<string>6D5C9F6B-6C2B-4A5C-8E1B-0A1C2D3E4F50</string>
        				<key>Keywords</key>
        				<array>
        					<string>Shell</string>
        					<string>Script</string>
        					<string>Command</string>
        					<string>Run</string>
        					<string>Unix</string>
        				</array>
        				<key>OutputUUID</key>
        				<string>6D5C9F6B-6C2B-4A5C-8E1B-0A1C2D3E4F51</string>
        				<key>UUID</key>
        				<string>6D5C9F6B-6C2B-4A5C-8E1B-0A1C2D3E4F52</string>
        				<key>UnlocalizedApplications</key>
        				<array>
        					<string>Automator</string>
        				</array>
        				<key>arguments</key>
        				<dict>
        					<key>0</key>
        					<dict>
        						<key>default value</key>
        						<integer>0</integer>
        						<key>name</key>
        						<string>inputMethod</string>
        						<key>required</key>
        						<string>0</string>
        						<key>type</key>
        						<string>0</string>
        						<key>uuid</key>
        						<string>0</string>
        					</dict>
        					<key>1</key>
        					<dict>
        						<key>default value</key>
        						<false/>
        						<key>name</key>
        						<string>CheckedForUserDefaultShell</string>
        						<key>required</key>
        						<string>0</string>
        						<key>type</key>
        						<string>0</string>
        						<key>uuid</key>
        						<string>1</string>
        					</dict>
        					<key>2</key>
        					<dict>
        						<key>default value</key>
        						<string></string>
        						<key>name</key>
        						<string>source</string>
        						<key>required</key>
        						<string>0</string>
        						<key>type</key>
        						<string>0</string>
        						<key>uuid</key>
        						<string>2</string>
        					</dict>
        					<key>3</key>
        					<dict>
        						<key>default value</key>
        						<string></string>
        						<key>name</key>
        						<string>COMMAND_STRING</string>
        						<key>required</key>
        						<string>0</string>
        						<key>type</key>
        						<string>0</string>
        						<key>uuid</key>
        						<string>3</string>
        					</dict>
        					<key>4</key>
        					<dict>
        						<key>default value</key>
        						<string>/bin/sh</string>
        						<key>name</key>
        						<string>shell</string>
        						<key>required</key>
        						<string>0</string>
        						<key>type</key>
        						<string>0</string>
        						<key>uuid</key>
        						<string>4</string>
        					</dict>
        				</dict>
        				<key>isViewVisible</key>
        				<integer>1</integer>
        				<key>location</key>
        				<string>309.000000:253.000000</string>
        				<key>nibPath</key>
        				<string>/System/Library/Automator/Run Shell Script.action/Contents/Resources/Base.lproj/main.nib</string>
        			</dict>
        			<key>isViewVisible</key>
        			<integer>1</integer>
        		</dict>
        	</array>
        	<key>connectors</key>
        	<dict/>
        	<key>workflowMetaData</key>
        	<dict>
        		<key>applicationBundleIDsByPath</key>
        		<dict/>
        		<key>applicationPaths</key>
        		<array/>
        		<key>inputTypeIdentifier</key>
        		<string>com.apple.Automator.fileSystemObject</string>
        		<key>outputTypeIdentifier</key>
        		<string>com.apple.Automator.nothing</string>
        		<key>presentationMode</key>
        		<integer>15</integer>
        		<key>processesInput</key>
        		<integer>0</integer>
        		<key>serviceInputTypeIdentifier</key>
        		<string>com.apple.Automator.fileSystemObject</string>
        		<key>serviceOutputTypeIdentifier</key>
        		<string>com.apple.Automator.nothing</string>
        		<key>serviceProcessesInput</key>
        		<integer>0</integer>
        		<key>systemImageName</key>
        		<string>NSActionTemplate</string>
        		<key>useAutomaticInputType</key>
        		<integer>0</integer>
        		<key>workflowTypeIdentifier</key>
        		<string>com.apple.Automator.servicesMenu</string>
        	</dict>
        </dict>
        </plist>

        """;

    public const string WorkflowInfo = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
        	<key>NSServices</key>
        	<array>
        		<dict>
        			<key>NSBackgroundColorName</key>
        			<string>background</string>
        			<key>NSIconName</key>
        			<string>NSActionTemplate</string>
        			<key>NSMenuItem</key>
        			<dict>
        				<key>default</key>
        				<string>FileAction…</string>
        			</dict>
        			<key>NSMessage</key>
        			<string>runWorkflowAsService</string>
        			<key>NSRequiredContext</key>
        			<dict>
        				<key>NSApplicationIdentifier</key>
        				<string>com.apple.finder</string>
        			</dict>
        			<key>NSSendFileTypes</key>
        			<array>
        				<string>public.item</string>
        			</array>
        		</dict>
        	</array>
        </dict>
        </plist>

        """;

    public static string LaunchAgentPlist(string command) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
        	<key>Label</key>
        	<string>{AgentLabel}</string>
        	<key>ProgramArguments</key>
        	<array>
        		<string>{Escape(command)}</string>
        		<string>--agent</string>
        	</array>
        	<key>RunAtLoad</key>
        	<true/>
        	<key>ProcessType</key>
        	<string>Interactive</string>
        </dict>
        </plist>

        """;
}
