using System;
using System.Collections.Generic;
using System.Globalization;

namespace SwissTechKnife.Core
{
    /// <summary>
    /// Host UI text in every supported language. English is the default; a missing translation falls back to it.
    /// The language is set once from the settings at startup and can change at runtime (<see cref="LanguageChanged"/>).
    /// </summary>
    public static class Strings
    {
        public const string English = "en";
        public const string PortugueseBrazil = "pt-BR";

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Shell
            ["Minimize"] = "Minimize",
            ["Close"] = "Close",
            ["CloseEsc"] = "Close (Esc)",
            ["Modules"] = "Modules",
            ["YourModules"] = "Your modules",
            ["SidebarEmpty"] = "Modules you install show up here.",
            ["UpdateAvailable"] = "Update available",
            ["UpdatesAvailable"] = "Updates available",
            ["Details"] = "Details",
            ["PageFailedTitle"] = "This module didn't open",
            ["DisableModule"] = "Disable module",
            ["TryAgain"] = "Try again",
            ["RestartNow"] = "Restart now",
            ["Restart"] = "Restart",
            ["ComingSoon"] = "Coming soon",
            ["Current"] = "Current",
            ["Cancel"] = "Cancel",
            ["Update"] = "Update",
            ["WhatsNew"] = "What's new",
            ["NoReleaseNotes"] = "This version has no published notes.",

            // Store
            ["StoreSubtitle"] = "Install only what you use. Each module runs inside SwissTechKnife, in the same process, and can be disabled or removed at any time.",
            ["AllTab"] = "All",
            ["AvailableTab"] = "Available",
            ["InstalledTab"] = "Installed",
            ["SearchModules"] = "Search modules",
            ["SeeAvailable"] = "See available",
            ["CatalogOffline"] = "No connection to the catalog. Showing the last saved list.",
            ["CatalogFailed"] = "Couldn't load the module catalog.",
            ["EmptySearchTitle"] = "Nothing found",
            ["EmptySearchText"] = "No module matches “{0}”.",
            ["EmptyInstalledTitle"] = "No modules installed",
            ["EmptyInstalledText"] = "Pick what you want to use in the Available tab.",
            ["EmptyFailedTitle"] = "Catalog unavailable",
            ["EmptyFailedText"] = "Check your connection and try again.",
            ["EmptyAllTitle"] = "All installed",
            ["EmptyAllText"] = "You already have every module in the catalog.",
            ["EmptyCatalogTitle"] = "No modules yet",
            ["EmptyCatalogText"] = "The catalog has no modules right now.",

            // Module dialog
            ["DescriptionTab"] = "Description",
            ["ReleaseNotesTab"] = "Release notes",
            ["Version"] = "Version",
            ["Size"] = "Size",
            ["Uninstall"] = "Uninstall",
            ["ConfirmRemoval"] = "Confirm removal",
            ["Open"] = "Open",
            ["Install"] = "Install",
            ["EnableModule"] = "Enable module",
            ["Enabled"] = "Enabled",
            ["Disabled"] = "Disabled",
            ["VersionAvailable"] = "v{0} available",
            ["ReleaseDateFormat"] = "MMM d, yyyy",

            // Module lifecycle
            ["Installing"] = "Installing…",
            ["Updating"] = "Updating…",
            ["ModuleInstalled"] = "{0} installed.",
            ["ModuleUpdated"] = "{0} updated.",
            ["ModuleUpdatedRestart"] = "{0} updated. Restart to apply.",
            ["ModuleUninstalled"] = "{0} uninstalled.",
            ["InstallFailed"] = "Couldn't install {0}.",
            ["VersionAfterRestart"] = "Version {0} will be used after restarting.",
            ["FilesRemovedOnRestart"] = "The files will be removed on restart.",
            ["ModuleStartFailed"] = "The module couldn't start: {0}",
            ["RestartForVersion"] = "Restart SwissTechKnife to use version {0}.",
            ["PageFailed"] = "The module page failed to open: {0}",
            ["NoSettingsInVersion"] = "This module has no settings in this version.",
            ["SettingsOpenFailed"] = "The settings couldn't be opened: {0}",
            ["SomethingWentWrong"] = "Something went wrong: {0}",

            // Update checks
            ["HostUpdateToast"] = "SwissTechKnife update available: {0}. See Settings.",
            ["ModuleUpdatesOne"] = "1 module update available.",
            ["ModuleUpdatesMany"] = "{0} module updates available.",
            ["AllUpToDate"] = "Everything is up to date.",
            ["ModuleUpToDate"] = "{0} is up to date.",
            ["UpdateCheckFailed"] = "Couldn't check for updates right now.",

            // Settings
            ["SettingsTitle"] = "Settings",
            ["SettingsSubtitle"] = "SwissTechKnife preferences. Changes apply right away.",
            ["SectionGeneral"] = "General",
            ["SectionUpdates"] = "Updates",
            ["SectionSystem"] = "System",
            ["Language"] = "Language",
            ["LanguageHint"] = "Language used by the SwissTechKnife interface.",
            ["HostUpdateAvailable"] = "SwissTechKnife update available",
            ["CheckForUpdates"] = "Check for updates",
            ["CheckForUpdatesHint"] = "How often the catalog is checked for new versions of SwissTechKnife and its modules.",
            ["CheckNow"] = "Check now",
            ["Daily"] = "Daily",
            ["Weekly"] = "Weekly",
            ["Monthly"] = "Monthly",
            ["Never"] = "Never",
            ["Checking"] = "Checking…",
            ["NeverChecked"] = "Not checked yet",
            ["LastCheck"] = "Last checked: {0} at {1}",
            ["Today"] = "today",
            ["Yesterday"] = "yesterday",
            ["ShortDateFormat"] = "MMM d",
            ["CheckFailed"] = "Couldn't check: {0}",
            ["StartWithWindows"] = "Start with Windows",
            ["StartWithWindowsHint"] = "Opens SwissTechKnife when you sign in to Windows, so your modules are already running.",
            ["StartupChangeFailed"] = "Couldn't change the Windows startup setting.",
            ["HideToTray"] = "Hide to the notification area on close",
            ["HideToTrayHint"] = "Closing the window keeps SwissTechKnife and its modules running. Open it from the notification area icon; to quit, use “Exit” in its menu.",

            // Host self-update
            ["HostUpdateTitle"] = "Update SwissTechKnife",
            ["HostUpdateHint"] = "SwissTechKnife closes, installs the new version and reopens on its own. Windows may ask for permission to install.",
            ["HostUpdated"] = "SwissTechKnife updated to v{0}.",
            ["HostUpdateIncomplete"] = "The update to v{0} didn't complete. Nothing was changed; try again. Installer log: {1}",
            ["HostUpdateIncompleteToast"] = "The SwissTechKnife update didn't complete.",
            ["DownloadingUpdate"] = "Downloading the update…",
            ["WaitingForPermission"] = "Waiting for Windows permission to install…",
            ["InstallerCanceled"] = "The installer was canceled or couldn't run (code {0}). Nothing was changed.",
            ["HostUpdateCanceledToast"] = "SwissTechKnife update canceled.",
            ["InstallingHost"] = "Installing. SwissTechKnife will close and reopen on its own.",
            ["HostUpdateFailedToast"] = "Couldn't update SwissTechKnife.",
            ["HostUpdateFailed"] = "The update failed: {0}",
            ["UpdateInvalid"] = "The catalog announces an invalid update.",
            ["UpdateHashMismatch"] = "The downloaded installer doesn't match the catalog (SHA-256). Nothing was changed.",
            ["UpdateDownloadFailed"] = "Couldn't download the update. Check your connection and try again.",
            ["DownloadIncomplete"] = "The download was incomplete. Try again.",
            ["InstallerChanged"] = "The downloaded installer was changed or removed. Try again.",
            ["InstallerNotStarted"] = "The installer didn't start.",
            ["InstallerNotStartedDetail"] = "The installer didn't start: {0}",

            // Packages
            ["PackageNotPublished"] = "This module doesn't have a published package yet.",
            ["PackageHashMismatch"] = "The downloaded package doesn't match the catalog (SHA-256). Nothing was changed.",
            ["PackageDownloadFailed"] = "Couldn't download the module. Check your connection and try again.",
            ["PackageCorrupt"] = "The module package is corrupted.",
            ["PackageInstallFailed"] = "The installation failed: {0}",
            ["PackageBadPaths"] = "The package contains invalid paths and was rejected.",
            ["PackageTooLarge"] = "The package is too large and was rejected.",
            ["PackageNoManifest"] = "The package doesn't contain module.json.",
            ["PackageWrongModule"] = "The package belongs to another module and was rejected.",
            ["PackageBadVersion"] = "The package has an invalid version.",
            ["PackageNeedsNewerHost"] = "This module needs a newer version of SwissTechKnife. Update the app first.",
            ["PackageBadEntry"] = "The package doesn't point to a valid entry point.",

            // App
            ["StartupFailed"] = "SwissTechKnife couldn't open.\n\n{0}",
            ["LoadModulesFailed"] = "Couldn't load the installed modules.",
            ["UnexpectedError"] = "An unexpected error occurred and was logged.",
            ["TrayOpen"] = "Open SwissTechKnife",
            ["TrayExit"] = "Exit",
            ["TrayHint"] = "Still running in the notification area so background modules keep working. To quit, use Exit here.",
            ["TrayOpenModule"] = "Open {0}",
            ["ModuleTrayIcon"] = "Show icon in the notification area",
            ["ModuleTrayIconHint"] = "Adds this module’s own icon with its commands. It stays while the module is enabled; turn the module off to remove it.",
        };

        private static readonly Dictionary<string, string> PtBr = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Minimize"] = "Minimizar",
            ["Close"] = "Fechar",
            ["CloseEsc"] = "Fechar (Esc)",
            ["Modules"] = "Módulos",
            ["YourModules"] = "Seus módulos",
            ["SidebarEmpty"] = "Os módulos que você instalar aparecem aqui.",
            ["UpdateAvailable"] = "Atualização disponível",
            ["UpdatesAvailable"] = "Atualizações disponíveis",
            ["Details"] = "Detalhes",
            ["PageFailedTitle"] = "Este módulo não abriu",
            ["DisableModule"] = "Desativar módulo",
            ["TryAgain"] = "Tentar de novo",
            ["RestartNow"] = "Reiniciar agora",
            ["Restart"] = "Reiniciar",
            ["ComingSoon"] = "Em breve",
            ["Current"] = "Atual",
            ["Cancel"] = "Cancelar",
            ["Update"] = "Atualizar",
            ["WhatsNew"] = "O que muda",
            ["NoReleaseNotes"] = "Esta versão não tem notas publicadas.",

            ["StoreSubtitle"] = "Instale só o que você usa. Cada módulo roda dentro do SwissTechKnife, no mesmo processo, e pode ser desativado ou removido a qualquer momento.",
            ["AllTab"] = "Todos",
            ["AvailableTab"] = "Disponíveis",
            ["InstalledTab"] = "Instalados",
            ["SearchModules"] = "Buscar módulos",
            ["SeeAvailable"] = "Ver disponíveis",
            ["CatalogOffline"] = "Sem conexão com o catálogo. Mostrando a última lista salva.",
            ["CatalogFailed"] = "Não foi possível carregar o catálogo de módulos.",
            ["EmptySearchTitle"] = "Nada encontrado",
            ["EmptySearchText"] = "Nenhum módulo corresponde a “{0}”.",
            ["EmptyInstalledTitle"] = "Nenhum módulo instalado",
            ["EmptyInstalledText"] = "Escolha o que você quer usar na aba Disponíveis.",
            ["EmptyFailedTitle"] = "Catálogo indisponível",
            ["EmptyFailedText"] = "Verifique a conexão e tente de novo.",
            ["EmptyAllTitle"] = "Tudo instalado",
            ["EmptyAllText"] = "Você já tem todos os módulos do catálogo.",
            ["EmptyCatalogTitle"] = "Nenhum módulo ainda",
            ["EmptyCatalogText"] = "O catálogo não tem módulos no momento.",

            ["DescriptionTab"] = "Descrição",
            ["ReleaseNotesTab"] = "Notas da versão",
            ["Version"] = "Versão",
            ["Size"] = "Tamanho",
            ["Uninstall"] = "Desinstalar",
            ["ConfirmRemoval"] = "Confirmar remoção",
            ["Open"] = "Abrir",
            ["Install"] = "Instalar",
            ["EnableModule"] = "Ativar módulo",
            ["Enabled"] = "Ativado",
            ["Disabled"] = "Desativado",
            ["VersionAvailable"] = "v{0} disponível",
            ["ReleaseDateFormat"] = "d MMM yyyy",

            ["Installing"] = "Instalando…",
            ["Updating"] = "Atualizando…",
            ["ModuleInstalled"] = "{0} instalado.",
            ["ModuleUpdated"] = "{0} atualizado.",
            ["ModuleUpdatedRestart"] = "{0} atualizado. Reinicie para aplicar.",
            ["ModuleUninstalled"] = "{0} desinstalado.",
            ["InstallFailed"] = "Falha ao instalar {0}.",
            ["VersionAfterRestart"] = "A versão {0} será usada após reiniciar.",
            ["FilesRemovedOnRestart"] = "Os arquivos serão removidos ao reiniciar.",
            ["ModuleStartFailed"] = "O módulo não conseguiu iniciar: {0}",
            ["RestartForVersion"] = "Reinicie o SwissTechKnife para usar a versão {0}.",
            ["PageFailed"] = "A página do módulo falhou ao abrir: {0}",
            ["NoSettingsInVersion"] = "Este módulo não oferece configurações nesta versão.",
            ["SettingsOpenFailed"] = "As configurações não puderam ser abertas: {0}",
            ["SomethingWentWrong"] = "Algo deu errado: {0}",

            ["HostUpdateToast"] = "Atualização do SwissTechKnife disponível: {0}. Veja em Configurações.",
            ["ModuleUpdatesOne"] = "1 atualização de módulo disponível.",
            ["ModuleUpdatesMany"] = "{0} atualizações de módulos disponíveis.",
            ["AllUpToDate"] = "Tudo atualizado.",
            ["ModuleUpToDate"] = "{0} está atualizado.",
            ["UpdateCheckFailed"] = "Não foi possível verificar atualizações agora.",

            ["SettingsTitle"] = "Configurações",
            ["SettingsSubtitle"] = "Preferências do SwissTechKnife. As alterações valem na hora.",
            ["SectionGeneral"] = "Geral",
            ["SectionUpdates"] = "Atualizações",
            ["SectionSystem"] = "Sistema",
            ["Language"] = "Idioma",
            ["LanguageHint"] = "Idioma usado na interface do SwissTechKnife.",
            ["HostUpdateAvailable"] = "Atualização do SwissTechKnife disponível",
            ["CheckForUpdates"] = "Verificar atualizações",
            ["CheckForUpdatesHint"] = "Com que frequência o catálogo é consultado em busca de novas versões do SwissTechKnife e dos módulos.",
            ["CheckNow"] = "Verificar agora",
            ["Daily"] = "Diário",
            ["Weekly"] = "Semanal",
            ["Monthly"] = "Mensal",
            ["Never"] = "Nunca",
            ["Checking"] = "Verificando…",
            ["NeverChecked"] = "Ainda não verificado",
            ["LastCheck"] = "Última verificação: {0} às {1}",
            ["Today"] = "hoje",
            ["Yesterday"] = "ontem",
            ["ShortDateFormat"] = "dd 'de' MMM",
            ["CheckFailed"] = "Não foi possível verificar: {0}",
            ["StartWithWindows"] = "Iniciar com o Windows",
            ["StartWithWindowsHint"] = "Abre o SwissTechKnife ao entrar no Windows, para que os módulos já estejam rodando.",
            ["StartupChangeFailed"] = "Não foi possível alterar a inicialização com o Windows.",
            ["HideToTray"] = "Esconder na área de notificação ao fechar",
            ["HideToTrayHint"] = "Fechar a janela mantém o SwissTechKnife e os módulos rodando. Abra pelo ícone na área de notificação; para sair, use “Sair” no menu dele.",

            ["HostUpdateTitle"] = "Atualizar o SwissTechKnife",
            ["HostUpdateHint"] = "O SwissTechKnife fecha, instala a nova versão e abre de novo sozinho. O Windows pode pedir permissão para instalar.",
            ["HostUpdated"] = "SwissTechKnife atualizado para v{0}.",
            ["HostUpdateIncomplete"] = "A atualização para v{0} não foi concluída. Nada foi alterado; tente de novo. Registro do instalador: {1}",
            ["HostUpdateIncompleteToast"] = "A atualização do SwissTechKnife não foi concluída.",
            ["DownloadingUpdate"] = "Baixando a atualização…",
            ["WaitingForPermission"] = "Aguardando a permissão do Windows para instalar…",
            ["InstallerCanceled"] = "O instalador foi cancelado ou não pôde ser executado (código {0}). Nada foi alterado.",
            ["HostUpdateCanceledToast"] = "Atualização do SwissTechKnife cancelada.",
            ["InstallingHost"] = "Instalando. O SwissTechKnife vai fechar e abrir de novo sozinho.",
            ["HostUpdateFailedToast"] = "Falha ao atualizar o SwissTechKnife.",
            ["HostUpdateFailed"] = "A atualização falhou: {0}",
            ["UpdateInvalid"] = "O catálogo anuncia uma atualização inválida.",
            ["UpdateHashMismatch"] = "O instalador baixado não confere com o catálogo (SHA-256). Nada foi alterado.",
            ["UpdateDownloadFailed"] = "Não foi possível baixar a atualização. Verifique a conexão e tente de novo.",
            ["DownloadIncomplete"] = "O download terminou incompleto. Tente de novo.",
            ["InstallerChanged"] = "O instalador baixado foi alterado ou removido. Tente de novo.",
            ["InstallerNotStarted"] = "O instalador não iniciou.",
            ["InstallerNotStartedDetail"] = "O instalador não iniciou: {0}",

            ["PackageNotPublished"] = "Este módulo ainda não tem um pacote publicado.",
            ["PackageHashMismatch"] = "O pacote baixado não confere com o catálogo (SHA-256). Nada foi alterado.",
            ["PackageDownloadFailed"] = "Não foi possível baixar o módulo. Verifique a conexão e tente de novo.",
            ["PackageCorrupt"] = "O pacote do módulo está corrompido.",
            ["PackageInstallFailed"] = "A instalação falhou: {0}",
            ["PackageBadPaths"] = "O pacote contém caminhos inválidos e foi recusado.",
            ["PackageTooLarge"] = "O pacote é grande demais e foi recusado.",
            ["PackageNoManifest"] = "O pacote não contém module.json.",
            ["PackageWrongModule"] = "O pacote pertence a outro módulo e foi recusado.",
            ["PackageBadVersion"] = "O pacote tem uma versão inválida.",
            ["PackageNeedsNewerHost"] = "Este módulo precisa de uma versão mais nova do SwissTechKnife. Atualize o app primeiro.",
            ["PackageBadEntry"] = "O pacote não aponta para um ponto de entrada válido.",

            ["StartupFailed"] = "O SwissTechKnife não conseguiu abrir.\n\n{0}",
            ["LoadModulesFailed"] = "Não foi possível carregar os módulos instalados.",
            ["UnexpectedError"] = "Um erro inesperado ocorreu e foi registrado no log.",
            ["TrayOpen"] = "Abrir SwissTechKnife",
            ["TrayExit"] = "Sair",
            ["TrayHint"] = "Continua na área de notificação para os módulos em segundo plano funcionarem. Para sair, use Sair aqui.",
            ["TrayOpenModule"] = "Abrir {0}",
            ["ModuleTrayIcon"] = "Exibir ícone na barra de notificação",
            ["ModuleTrayIconHint"] = "Adiciona o ícone próprio deste módulo com os comandos dele. Ele fica enquanto o módulo estiver ativado; desative o módulo para removê-lo.",
        };

        private static volatile Dictionary<string, string> _table = En;
        private static volatile CultureInfo _culture = CultureInfo.GetCultureInfo("en-US");
        private static volatile string _language = English;

        /// <summary>Raised on the thread that changed the language, after the new table is in place.</summary>
        public static event EventHandler LanguageChanged;

        /// <summary>Supported languages as (code, native name), in display order.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Languages { get; } = new[]
        {
            new KeyValuePair<string, string>(English, "English"),
            new KeyValuePair<string, string>(PortugueseBrazil, "Português (Brasil)"),
        };

        /// <summary>Current language code ("en" or "pt-BR").</summary>
        public static string Language => _language;

        /// <summary>Culture for dates and numbers in the current language.</summary>
        public static CultureInfo Culture => _culture;

        /// <summary>Maps a stored value to a supported language; anything unknown or empty is English.</summary>
        public static string Normalize(string language) =>
            string.Equals(language, PortugueseBrazil, StringComparison.OrdinalIgnoreCase) ? PortugueseBrazil : English;

        public static void SetLanguage(string language)
        {
            string normalized = Normalize(language);
            if (normalized == _language)
            {
                return;
            }
            _table = normalized == PortugueseBrazil ? PtBr : En;
            _culture = CultureInfo.GetCultureInfo(normalized == PortugueseBrazil ? "pt-BR" : "en-US");
            _language = normalized;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static string Get(string key)
        {
            if (key == null)
            {
                return string.Empty;
            }
            return _table.TryGetValue(key, out string text) || En.TryGetValue(key, out text) ? text : key;
        }

        public static string Format(string key, params object[] args) => string.Format(_culture, Get(key), args);

        /// <summary>Keys present in English but missing from another table (checked by the tests).</summary>
        internal static IEnumerable<string> MissingTranslations()
        {
            foreach (string key in En.Keys)
            {
                if (!PtBr.ContainsKey(key))
                {
                    yield return key;
                }
            }
        }
    }
}
