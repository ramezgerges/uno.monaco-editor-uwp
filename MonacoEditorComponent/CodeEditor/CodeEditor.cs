﻿using Collections.Generic;
using Monaco.Editor;
using Monaco.Extensions;
using Monaco.Helpers;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// The Templated Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234235

namespace Monaco
{
    /// <summary>
    /// UWP Windows Runtime Component wrapper for the Monaco CodeEditor
    /// https://microsoft.github.io/monaco-editor/
    /// </summary>
    [TemplatePart(Name = "View", Type = typeof(ICodeEditorPresenter))]
    public sealed partial class CodeEditor : Control, INotifyPropertyChanged, IDisposable
    {
        private bool _initialized;
        private ICodeEditorPresenter _view;
        private ModelHelper _model;
        private CssStyleBroker _cssBroker;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Template Property used during loading to prevent blank control visibility when it's still loading WebView.
        /// </summary>
        public bool IsEditorLoaded
        {
            get => (bool)GetValue(IsEditorLoadedProperty);
            private set => SetValue(IsEditorLoadedProperty, value);
        }

        public static DependencyProperty IsEditorLoadedProperty { get; } = DependencyProperty.Register(nameof(IsEditorLoaded), typeof(string), typeof(CodeEditor), new PropertyMetadata(false));

        /// <summary>
        /// Construct a new IStandAloneCodeEditor.
        /// </summary>
        public CodeEditor()
        {
            DefaultStyleKey = typeof(CodeEditor);

            Options = new StandaloneEditorConstructionOptions();
            //if (Options != null)
            //{
                // Set Pass-Thru Properties
                Options.GlyphMargin = HasGlyphMargin;

                //// Register for changes
                //Options.PropertyChanged += Options_PropertyChanged;
            //}

            // Initialize this here so property changed event will fire and register collection changed event.
            Decorations = new ObservableVector<IModelDeltaDecoration>();
            Markers = new ObservableVector<IMarkerData>();
            //_model = new ModelHelper(this);
            #pragma warning disable CS0618 // Type or member is obsolete
            Languages = new LanguagesHelper(this);
            #pragma warning restore CS0618 // Type or member is obsolete
            _cssBroker = new CssStyleBroker(this);

            base.Loaded += CodeEditor_Loaded;
            Unloaded += CodeEditor_Unloaded;
        }

        private async void Options_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!(sender is StandaloneEditorConstructionOptions options)) return;
            if (e.PropertyName == nameof(StandaloneEditorConstructionOptions.Language))
            {
                await InvokeScriptAsync("updateLanguage", options.Language);
                if (CodeLanguage != options.Language) CodeLanguage = options.Language;
            }
            if (e.PropertyName == nameof(StandaloneEditorConstructionOptions.GlyphMargin))
            {
                if (HasGlyphMargin != options.GlyphMargin) options.GlyphMargin = HasGlyphMargin;
            }
            if (e.PropertyName == nameof(StandaloneEditorConstructionOptions.ReadOnly))
            {
                if (ReadOnly != options.ReadOnly) options.ReadOnly = ReadOnly;
            }
            await InvokeScriptAsync("updateMonacoOptions", options);
        }

        private void CodeEditor_Loaded(object sender, RoutedEventArgs e)
        {
            // Do this the 2nd time around.
            if (_model == null && _view != null)
            {
                _model = new ModelHelper(this);

                //Options.PropertyChanged += Options_PropertyChanged;
                Debug.WriteLine("Connecting options property changed");

                Decorations.VectorChanged += Decorations_VectorChanged;
                Markers.VectorChanged += Markers_VectorChanged;

                _view.NewWindowRequested += WebView_NewWindowRequested;
                _view.Loaded += WebView_DOMContentLoaded;

                Debug.WriteLine("Setting initialized - true");
                _initialized = true;

                Loading?.Invoke(this, new RoutedEventArgs());

                Unloaded += CodeEditor_Unloaded;

                Loaded?.Invoke(this, new RoutedEventArgs());
            }
        }

        private void CodeEditor_Unloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= CodeEditor_Unloaded;

            if (_view != null)
            {
                _view.NavigationStarting -= WebView_NavigationStarting;
                _view.NavigationCompleted -= WebView_NavigationCompleted;
                _view.NewWindowRequested -= WebView_NewWindowRequested;
                _view.Loaded -= WebView_DOMContentLoaded;
                Debug.WriteLine("Setting initialized - false");
                _initialized = false;
            }

            Decorations.VectorChanged -= Decorations_VectorChanged;
            Markers.VectorChanged -= Markers_VectorChanged;

            Options.PropertyChanged -= Options_PropertyChanged;

            if (_themeListener != null)
            {
                _themeListener.ThemeChanged -= ThemeListener_ThemeChanged;
            }
            _themeListener = null;
            
            UnregisterPropertyChangedCallback(RequestedThemeProperty, _themeToken);
            _keyboardListener = null;
            _model = null;
        }

        protected override void OnApplyTemplate()
        {
            if (_view != null)
            {
                _view.NavigationStarting -= WebView_NavigationStarting;
                _view.NavigationCompleted -= WebView_NavigationCompleted;
                _view.NewWindowRequested -= WebView_NewWindowRequested;
                _view.Loaded -= WebView_DOMContentLoaded;
                Debug.WriteLine("Setting initialized - false");
                _initialized = false;
            }

            _view = (ICodeEditorPresenter)GetTemplateChild("View");

            if (_view != null)
            {
                _view.NavigationStarting += WebView_NavigationStarting;
                _view.NavigationCompleted += WebView_NavigationCompleted;
                _view.NewWindowRequested += WebView_NewWindowRequested;

                if (_view.IsLoaded)
                {
                    WebView_DOMContentLoaded();
                }
                else
                {
                    _view.Loaded += WebView_DOMContentLoaded;
                }

#if __WASM__
                //_view.Source = new System.Uri("ms-appx-web:///Monaco/CodeEditor/CodeEditor.html");
#else
                _view.Source = new System.Uri("ms-appx-web:///Monaco/CodeEditor/CodeEditor.html");
#endif
                //_view.Source = new System.Uri("file:///MonacoCodeEditor.html", UriKind.RelativeOrAbsolute);
            }

            base.OnApplyTemplate();

#if __WASM__
            CodeEditor_Loaded(this, null);
#endif
        }

        internal async Task SendScriptAsync(string script,
            [CallerMemberName] string member = null,
            [CallerFilePath] string file = null,
            [CallerLineNumber] int line = 0)
        {
            await SendScriptAsync<object>(script, member, file, line);
        }

        internal async Task<T> SendScriptAsync<T>(string script,
            [CallerMemberName] string member = null,
            [CallerFilePath] string file = null,
            [CallerLineNumber] int line = 0)
        {
            if (_initialized)
            {
                try
                {
                    return await _view.RunScriptAsync<T>(script, member, file, line);
                }
                catch (Exception e)
                {
                    InternalException?.Invoke(this, e);
                }
            }
            else
            {
#if DEBUG
                Debug.WriteLine("WARNING: Tried to call '" + script + "' before initialized.");
#endif
            }

            return default;
        }

        internal async Task InvokeScriptAsync(
            string method,
            object arg,
            bool serialize = true,
            [CallerMemberName] string member = null,
            [CallerFilePath] string file = null,
            [CallerLineNumber] int line = 0)
        {
            await InvokeScriptAsync<object>(method, new object[] { arg }, serialize, member, file, line);
        }

        internal async Task InvokeScriptAsync(
            string method,
            object[] args,
            bool serialize = true,
            [CallerMemberName] string member = null,
            [CallerFilePath] string file = null,
            [CallerLineNumber] int line = 0)
        {
            await InvokeScriptAsync<object>(method, args, serialize, member, file, line);
        }

        internal async Task<T> InvokeScriptAsync<T>(
            string method,
            object arg,
            bool serialize = true,
            [CallerMemberName] string member = null,
            [CallerFilePath] string file = null,
            [CallerLineNumber] int line = 0)
        {
            return await InvokeScriptAsync<T>(method, new object[] { arg }, serialize, member, file, line);
        }

        internal async Task<T> InvokeScriptAsync<T>(
            string method,
            object[] args,
            bool serialize = true,
            [CallerMemberName] string member = null,
            [CallerFilePath] string file = null,
            [CallerLineNumber] int line = 0)
        {
            if (_initialized)
            {
                try
                {
                    return await _view.InvokeScriptAsync<T>(method, args, serialize, member, file, line);
                }
                catch (Exception e)
                {
                    InternalException?.Invoke(this, e);
                }
            }
            else
            {
#if DEBUG
                Debug.WriteLine("WARNING: Tried to call " + method + " before initialized.");
#endif
            }

            return default;
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _cssBroker?.Dispose();
            _cssBroker = null;
            _parentAccessor?.Dispose();
            _parentAccessor = null;
        }
    }

    public static class UriHelper
    {
        private static readonly string UNO_BOOTSTRAP_APP_BASE = global::System.Environment.GetEnvironmentVariable(nameof(UNO_BOOTSTRAP_APP_BASE));
        private static readonly string UNO_BOOTSTRAP_WEBAPP_BASE_PATH = Environment.GetEnvironmentVariable(nameof(UNO_BOOTSTRAP_WEBAPP_BASE_PATH)) ?? "";

        public static string AbsoluteUriString(this System.Uri uri)
        {
            string target;
            if (uri.IsAbsoluteUri)
            {
#if __WASM__
                if (uri.Scheme == "file" || uri.Scheme== "ms-appx-web")
                {
                    // Local files are assumed as coming from the remoter server
                    target = UNO_BOOTSTRAP_APP_BASE == null ? uri.PathAndQuery : UNO_BOOTSTRAP_WEBAPP_BASE_PATH + UNO_BOOTSTRAP_APP_BASE + uri.PathAndQuery;
                }
                else
                {
                    target = uri.AbsoluteUri;
                }
#else
                target = uri.AbsoluteUri;
#endif
            }
            else
            {
                target = UNO_BOOTSTRAP_APP_BASE == null
                    ? uri.OriginalString
                    : UNO_BOOTSTRAP_WEBAPP_BASE_PATH + UNO_BOOTSTRAP_APP_BASE + "/" + uri.OriginalString;
            }
            return target;
        }
    }
}
