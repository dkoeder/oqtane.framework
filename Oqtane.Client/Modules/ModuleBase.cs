using Microsoft.AspNetCore.Components;
using Oqtane.Shared;
using Oqtane.Models;
using System.Threading.Tasks;
using Oqtane.Services;
using System;
using Oqtane.Enums;
using Oqtane.UI;
using System.Collections.Generic;
using Microsoft.JSInterop;
using System.Linq;

namespace Oqtane.Modules
{
    public abstract class ModuleBase : ComponentBase, IModuleControl
    {
        private Logger _logger;

        protected Logger logger => _logger ?? (_logger = new Logger(this));

        [Inject]
        protected ILogService LoggingService { get; set; }

        [Inject]
        protected IJSRuntime JSRuntime { get; set; }

        [CascadingParameter]
        protected PageState PageState { get; set; }

        [CascadingParameter]
        protected Module ModuleState { get; set; }

        [Parameter]
        public ModuleInstance ModuleInstance { get; set; }

        // optional interface properties
        public virtual SecurityAccessLevel SecurityAccessLevel { get { return SecurityAccessLevel.View; } set { } } // default security

        public virtual string Title { get { return ""; } }

        public virtual string Actions { get { return ""; } }

        public virtual bool UseAdminContainer { get { return true; } }

        public virtual List<Resource> Resources { get; set; }

        // base lifecycle method for handling JSInterop script registration

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                if (Resources != null && Resources.Exists(item => item.ResourceType == ResourceType.Script))
                {
                    var scripts = new List<object>();
                    foreach (Resource resource in Resources.Where(item => item.ResourceType == ResourceType.Script))
                    {
                        var url = (resource.Url.Contains("://")) ? resource.Url : PageState.Alias.BaseUrl + "/" + resource.Url;
                        scripts.Add(new { href = url, bundle = resource.Bundle ?? "", integrity = resource.Integrity ?? "", crossorigin = resource.CrossOrigin ?? "", es6module = resource.ES6Module });
                    }
                    if (scripts.Any())
                    {
                        var interop = new Interop(JSRuntime);
                        await interop.IncludeScripts(scripts.ToArray());
                    }
                }
            }
        }

        // path method

        public string ModulePath()
        {
            return "Modules/" + GetType().Namespace + "/";
        }

        // url methods
        public string NavigateUrl()
        {
            return NavigateUrl(PageState.Page.Path);
        }

        public string NavigateUrl(string path)
        {
            return NavigateUrl(path, "");
        }

        public string NavigateUrl(bool refresh)
        {
            return NavigateUrl(PageState.Page.Path, refresh);
        }

        public string NavigateUrl(string path, string parameters)
        {
            return Utilities.NavigateUrl(PageState.Alias.Path, path, parameters);
        }

        public string NavigateUrl(string path, bool refresh)
        {
            return Utilities.NavigateUrl(PageState.Alias.Path, path, refresh ? "refresh" : "");
        }

        public string EditUrl(string action)
        {
            return EditUrl(ModuleState.ModuleId, action);
        }

        public string EditUrl(string action, string parameters)
        {
            return EditUrl(ModuleState.ModuleId, action, parameters);
        }

        public string EditUrl(int moduleId, string action)
        {
            return EditUrl(moduleId, action, "");
        }

        public string EditUrl(int moduleId, string action, string parameters)
        {
            return EditUrl(PageState.Page.Path, moduleId, action, parameters);
        }

        public string EditUrl(string path, int moduleid, string action, string parameters)
        {
            return Utilities.EditUrl(PageState.Alias.Path, path, moduleid, action, parameters);
        }

        public string ContentUrl(int fileid)
        {
            return ContentUrl(fileid, false);
        }

        public string ContentUrl(int fileid, bool asAttachment)
        {
            return Utilities.ContentUrl(PageState.Alias, fileid, asAttachment);
        }

        public string ImageUrl(int fileid, int width, int height)
        {
            return ImageUrl(fileid, width, height, "");
        }

        public string ImageUrl(int fileid, int width, int height, string mode)
        {
            return ImageUrl(fileid, width, height, mode, "", "", 0, false);
        }

        public string ImageUrl(int fileid, int width, int height, string mode, string position, string background, int rotate, bool recreate)
        {
            return Utilities.ImageUrl(PageState.Alias, fileid, width, height, mode, position, background, rotate, recreate);
        }

        public virtual Dictionary<string, string> GetUrlParameters(string parametersTemplate = "")
        {
            var urlParameters = new Dictionary<string, string>();
            string[] templateSegments;
            var parameters = PageState.UrlParameters.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var parameterId = 0;

            if (string.IsNullOrEmpty(parametersTemplate))
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    urlParameters.TryAdd("parameter" + i, parameters[i]);
                }
            }
            else
            {
                templateSegments = parametersTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (parameters.Length == templateSegments.Length)
                {
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (parameters.Length > i)
                        {
                            if (templateSegments[i] == parameters[i])
                            {
                                urlParameters.TryAdd("parameter" + parameterId, parameters[i]);
                                parameterId++;
                            }
                            else if (templateSegments[i].StartsWith("{") && templateSegments[i].EndsWith("}"))
                            {
                                var key = templateSegments[i].Replace("{", "");
                                key = key.Replace("}", "");
                                urlParameters.TryAdd(key, parameters[i]);
                            }
                            else
                            {
                                i = parameters.Length;
                                urlParameters.Clear();
                            }
                        }
                    }
                }
            }

            return urlParameters;
        }

        // user feedback methods
        public void AddModuleMessage(string message, MessageType type)
        {
            ModuleInstance.AddModuleMessage(message, type);
        }

        public void ClearModuleMessage()
        {
            ModuleInstance.AddModuleMessage("", MessageType.Undefined);
        }

        public void ShowProgressIndicator()
        {
            ModuleInstance.ShowProgressIndicator();
        }

        public void HideProgressIndicator()
        {
            ModuleInstance.HideProgressIndicator();
        }

        // logging methods
        public async Task Log(Alias alias, LogLevel level, string function, Exception exception, string message, params object[] args)
        {
            LogFunction logFunction;
            if (string.IsNullOrEmpty(function))
            {
                // try to infer from page action
                function = PageState.Action;
            }
            if (!Enum.TryParse(function, out logFunction))
            {
                switch (function.ToLower())
                {
                    case "add":
                        logFunction = LogFunction.Create;
                        break;
                    case "edit":
                        logFunction = LogFunction.Update;
                        break;
                    case "delete":
                        logFunction = LogFunction.Delete;
                        break;
                    case "":
                        logFunction = LogFunction.Read;
                        break;
                    default:
                        logFunction = LogFunction.Other;
                        break;
                }
            }
            await Log(alias, level, logFunction, exception, message, args);
        }

        public async Task Log(Alias alias, LogLevel level, LogFunction function, Exception exception, string message, params object[] args)
        {
            int pageId = ModuleState.PageId;
            int moduleId = ModuleState.ModuleId;
            int? userId = null;
            if (PageState.User != null)
            {
                userId = PageState.User.UserId;
            }
            string category = GetType().AssemblyQualifiedName;
            string feature = Utilities.GetTypeNameLastSegment(category, 1);

            await LoggingService.Log(alias, pageId, moduleId, userId, category, feature, function, level, exception, message, args);
        }

        public class Logger
        {
            private readonly ModuleBase _moduleBase;

            public Logger(ModuleBase moduleBase)
            {
                _moduleBase = moduleBase;
            }

            public async Task LogTrace(string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Trace, "", null, message, args);
            }

            public async Task LogTrace(LogFunction function, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Trace, function, null, message, args);
            }

            public async Task LogTrace(Exception exception, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Trace, "", exception, message, args);
            }

            public async Task LogDebug(string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Debug, "", null, message, args);
            }

            public async Task LogDebug(LogFunction function, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Debug, function, null, message, args);
            }

            public async Task LogDebug(Exception exception, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Debug, "", exception, message, args);
            }

            public async Task LogInformation(string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Information, "", null, message, args);
            }

            public async Task LogInformation(LogFunction function, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Information, function, null, message, args);
            }

            public async Task LogInformation(Exception exception, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Information, "", exception, message, args);
            }

            public async Task LogWarning(string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Warning, "", null, message, args);
            }

            public async Task LogWarning(LogFunction function, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Warning, function, null, message, args);
            }

            public async Task LogWarning(Exception exception, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Warning, "", exception, message, args);
            }

            public async Task LogError(string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Error, "", null, message, args);
            }

            public async Task LogError(LogFunction function, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Error, function, null, message, args);
            }

            public async Task LogError(Exception exception, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Error, "", exception, message, args);
            }

            public async Task LogCritical(string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Critical, "", null, message, args);
            }

            public async Task LogCritical(LogFunction function, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Critical, function, null, message, args);
            }

            public async Task LogCritical(Exception exception, string message, params object[] args)
            {
                await _moduleBase.Log(null, LogLevel.Critical, "", exception, message, args);
            }
        }
    }
}
