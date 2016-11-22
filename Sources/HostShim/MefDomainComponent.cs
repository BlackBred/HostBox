﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Threading;
using System.Threading.Tasks;

using HostBox.Borderline;

namespace HostShim
{
    /// <summary>
    /// Загрузчик, который устанавливается в домен приложения и выполняет загрузку компонентов приложения. 
    /// </summary>
    public class MefDomainComponent : MarshalByRefObject, IComponent
    {
        private static readonly ManualResetEvent CanStopComponentEvent = new ManualResetEvent(true);

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="MefDomainComponent"/>. 
        /// </summary>
        /// <param name="componentPath">  Путь к исполняемым модулям компонента. </param>
        public MefDomainComponent(string componentPath)
        {
            this.Path = componentPath;
        }

        /// <inheritdoc />
        public string Path { get; }

        [ImportMany(typeof(IHostableComponentFactory))]
        private IEnumerable<IHostableComponentFactory> HostableComponentFactories { get; set; }

        private IEnumerable<IHostableComponent> HostableComponent { get; set; }

        /// <inheritdoc />
        public void Start()
        {
            Directory.SetCurrentDirectory(this.Path);
            TaskScheduler.UnobservedTaskException += this.UnobservedTaskException;

            string asyncStart = ConfigurationManager.AppSettings[nameof(asyncStart)] ?? false.ToString();
            if (asyncStart.Equals(false.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                this.StartInternal();
                return;
            }

            CanStopComponentEvent.Reset();
            Task.Factory.StartNew(this.StartInternal, TaskCreationOptions.LongRunning)
                .ContinueWith(t => CanStopComponentEvent.Set());
        }

        /// <inheritdoc />
        public void Resume()
        {
            CanStopComponentEvent.WaitOne();

            foreach (var hostableComponent in this.HostableComponent)
            {
                hostableComponent.Resume();
            }
        }

        /// <inheritdoc />
        public void Pause()
        {
            CanStopComponentEvent.WaitOne();

            foreach (var hostableComponent in this.HostableComponent)
            {
                hostableComponent.Pause();
            }
        }

        /// <inheritdoc />
        public void Stop()
        {
            CanStopComponentEvent.WaitOne();

            foreach (var hostableComponent in this.HostableComponent)
            {
                hostableComponent.Stop();
            }
        }

        /// <inheritdoc />
        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.Infrastructure)]
        public override object InitializeLifetimeService()
        {
            return null;
        }

        private void StartInternal()
        {
            this.LoadComponentFactories();
            this.CreateComponents();
            this.StartComponents();
        }

        private void StartComponents()
        {
            foreach (var hostableComponent in this.HostableComponent)
            {
                hostableComponent.Start();
            }
        }

        private void CreateComponents()
        {
            if (this.HostableComponentFactories == null)
            {
                this.HostableComponent = Enumerable.Empty<IHostableComponent>();
                return;
            }

            var factories = this.HostableComponentFactories.ToList();
            var components = factories.Select(hostableComponentFactory => hostableComponentFactory.CreateComponent()).ToList();
            this.HostableComponent = components;
        }

        private void LoadComponentFactories()
        {
            var catalog = new DirectoryCatalog(this.Path);
            var container = new CompositionContainer(catalog);
            container.ComposeParts(this);
        }

        private void UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
        }
    }
}
