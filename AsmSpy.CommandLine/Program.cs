using AsmSpy.Core;
using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AsmSpy.CommandLine
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var commandLineApplication = new CommandLineApplication(throwOnUnexpectedArg: true);
            var directoryOrFile = commandLineApplication.Argument("directoryOrFile", "The directory to search for assemblies or file path to a single assembly");

            var silent = commandLineApplication.Option("-s|--silent", "Do not show any message, only warnings and errors will be shown.", CommandOptionType.NoValue);

            var nonsystem = commandLineApplication.Option("-n|--nonsystem", "Ignore 'System' assemblies", CommandOptionType.NoValue);
            var all = commandLineApplication.Option("-a|--all", "List all assemblies and references.", CommandOptionType.NoValue);
            var referencedStartsWith = commandLineApplication.Option("-rsw|--referencedstartswith", "Referenced Assembly should start with <string>. Will only analyze assemblies if their referenced assemblies starts with the given value.", CommandOptionType.SingleValue);

            var includeSubDirectories = commandLineApplication.Option("-i|--includesub", "Include subdirectories in search", CommandOptionType.NoValue);
            var configurationFile = commandLineApplication.Option("-c|--configurationFile", "Use the binding redirects of the given configuration file (Web.config or App.config)", CommandOptionType.SingleValue);
            var failOnMissing = commandLineApplication.Option("-f|--failOnMissing", "Whether to exit with an error code when AsmSpy detected Assemblies which could not be found", CommandOptionType.NoValue);

            var dependencyVisualizers = GetDependencyVisualizers();
            foreach(var visualizer in dependencyVisualizers)
            {
                visualizer.CreateOption(commandLineApplication);
            }

            commandLineApplication.HelpOption("-? | -h | --help");

            commandLineApplication.OnExecute(() =>
            {

                var consoleLogger = new ConsoleLogger(!silent.HasValue());

                var directoryOrFilePath = directoryOrFile.Value;
                var directoryPath = directoryOrFile.Value;

                if (!File.Exists(directoryOrFilePath) && !Directory.Exists(directoryOrFilePath))
                {
                    consoleLogger.LogMessage(string.Format(CultureInfo.InvariantCulture, "Directory or file: '{0}' does not exist.", directoryOrFilePath));
                    return -1;
                }

                var configurationFilePath = configurationFile.Value();
                if (!string.IsNullOrEmpty(configurationFilePath) && !File.Exists(configurationFilePath))
                {
                    consoleLogger.LogMessage(string.Format(CultureInfo.InvariantCulture, "Directory or file: '{0}' does not exist.", configurationFilePath));
                    return -1;
                }

                var isFilePathProvided = false;
                var fileName = "";
                if (File.Exists(directoryOrFilePath))
                {
                    isFilePathProvided = true;
                    fileName = Path.GetFileName(directoryOrFilePath);
                    directoryPath = Path.GetDirectoryName(directoryOrFilePath);
                }

                var onlyConflicts = !all.HasValue();
                var skipSystem = nonsystem.HasValue();
                var searchPattern = includeSubDirectories.HasValue() ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                var visualizerOptions = new VisualizerOptions(
                        skipSystem,
                        onlyConflicts,
                        referencedStartsWith.HasValue() ? referencedStartsWith.Value() : string.Empty
                    );

                var directoryInfo = new DirectoryInfo(directoryPath);

                List<FileInfo> fileList;
                if (isFilePathProvided)
                {
                    fileList = directoryInfo.GetFiles(fileName, SearchOption.TopDirectoryOnly).ToList();
                    consoleLogger.LogMessage(string.Format(CultureInfo.InvariantCulture, "Check assemblies referenced in: {0}", directoryOrFilePath));
                }
                else
                {
                    fileList = directoryInfo.GetFiles("*.dll", searchPattern).Concat(directoryInfo.GetFiles("*.exe", searchPattern)).ToList();
                    consoleLogger.LogMessage(string.Format(CultureInfo.InvariantCulture, "Check assemblies in: {0}", directoryInfo));
                }

                AppDomain appDomainWithBindingRedirects = null;
                try
                {
                    var domaininfo = new AppDomainSetup
                    {
                        ConfigurationFile = configurationFilePath
                    };
                    appDomainWithBindingRedirects = AppDomain.CreateDomain("AppDomainWithBindingRedirects", null, domaininfo);
                }
                catch (Exception ex)
                {
                    consoleLogger.LogError($"Failed creating AppDomain from configuration file with message {ex.Message}");
                    return -1;
                }

                // IDependencyAnalyzer seems to be pointless polymorphism, and there's no logic to the
                // separation between constructor arguments and the argument to Analyze(..)
                IDependencyAnalyzer analyzer = new DependencyAnalyzer(fileList, appDomainWithBindingRedirects)
                {
                    SkipSystem = skipSystem,
                    ReferencedStartsWith = referencedStartsWith.HasValue() ? referencedStartsWith.Value() : string.Empty
                };
                var result = analyzer.Analyze(consoleLogger);

                foreach(var visualizer in dependencyVisualizers.Where(x => x.IsConfigured()))
                {
                    visualizer.Visualize(result, consoleLogger, visualizerOptions);
                }

                if (failOnMissing.HasValue() && result.MissingAssemblies.Any())
                {
                    return -1;
                }

                return 0;
            });

            try
            {
                if (args == null || args.Length == 0)
                {
                    commandLineApplication.ShowHelp();
                    return 0;
                }

                return commandLineApplication.Execute(args);
            }
            catch (CommandParsingException cpe)
            {
                Console.WriteLine(cpe.Message);
                commandLineApplication.ShowHelp();
                return -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static IDependencyVisualizer[] GetDependencyVisualizers() => new IDependencyVisualizer[]
        {
            new ConsoleVisualizer(),
            new DgmlExport(),
            new XmlExport(),
            new DotExport(),
            new BindingRedirectExport()
        };
    }
}
