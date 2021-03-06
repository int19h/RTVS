﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Common.Core;
using Microsoft.Common.Core.IO;
using Microsoft.Common.Core.OS;
using Microsoft.R.Containers.Docker;
using Microsoft.UnitTests.Core.FluentAssertions;
using Microsoft.UnitTests.Core.XUnit;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.R.Containers.Windows.Test {
    [ExcludeFromCodeCoverage]
    [Category.Threads]
    public class WindowsContainerTests {
        [Test]
        public async Task CreateAndDeleteContainerTest() {
            await TaskUtilities.SwitchToBackgroundThread();
            WindowsDockerService svc = new WindowsDockerService(new FileSystem(), new ProcessServices(), new RegistryImpl());
            var param = new ContainerCreateParameters("docker.io/kvnadig/rtvs-linux", "latest");
            var container = await svc.CreateContainerAsync(param, CancellationToken.None);
            var container2 = await svc.GetContainerAsync(container.Id, CancellationToken.None);
            container.Id.Should().Be(container2.Id);
            await svc.DeleteContainerAsync(container, CancellationToken.None);
            var containers = await svc.ListContainersAsync(true, CancellationToken.None);
            containers.Should().NotContain(container.Id.Substring(0, 12));
        }

        [Test]
        public async Task StartStopContainerTest() {
            await TaskUtilities.SwitchToBackgroundThread();
            WindowsDockerService svc = new WindowsDockerService(new FileSystem(), new ProcessServices(), new RegistryImpl());
            var param = new ContainerCreateParameters("docker.io/kvnadig/rtvs-linux", "latest");
            var container = await svc.CreateContainerAsync(param, CancellationToken.None);
            await svc.StartContainerAsync(container, CancellationToken.None);

            var runningContainers = await svc.ListContainersAsync(false, CancellationToken.None);
            runningContainers.Should().Contain(container.Id.Substring(0, 12));

            await svc.StopContainerAsync(container, CancellationToken.None);

            var runningContainers2 = await svc.ListContainersAsync(false, CancellationToken.None);
            runningContainers2.Should().NotContain(container.Id.Substring(0, 12));

            await svc.DeleteContainerAsync(container, CancellationToken.None);
            var allContainers = await svc.ListContainersAsync(true, CancellationToken.None);
            allContainers.Should().NotContain(container.Id.Substring(0, 12));
        }

        [Test]
        public async Task CleanImageDownloadTest() {
            await TaskUtilities.SwitchToBackgroundThread();
            WindowsDockerService svc = new WindowsDockerService(new FileSystem(), new ProcessServices(), new RegistryImpl());

            var param = new ContainerCreateParameters("hello-world", "latest");
            string imageName = $"{param.Image}:{param.Tag}";
            await DeleteImageAsync(imageName);

            var container = await svc.CreateContainerAsync(param, CancellationToken.None);
            await svc.StartContainerAsync(container, CancellationToken.None);

            var runningContainers = await svc.ListContainersAsync(false, CancellationToken.None);
            runningContainers.Should().Contain(container.Id.Substring(0, 12));

            await svc.StopContainerAsync(container, CancellationToken.None);

            var runningContainers2 = await svc.ListContainersAsync(false, CancellationToken.None);
            runningContainers2.Should().NotContain(container.Id.Substring(0, 12));

            await svc.DeleteContainerAsync(container, CancellationToken.None);
            var allContainers = await svc.ListContainersAsync(true, CancellationToken.None);
            allContainers.Should().NotContain(container.Id.Substring(0, 12));

            await DeleteImageAsync(imageName);
        }

        private async Task<bool> DeleteImageAsync(string image) {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "docker";
            psi.Arguments = $"rmi -f {image}";
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;

            var process = Process.Start(psi);
            process.WaitForExit();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            return !string.IsNullOrEmpty(error);
        }
    }
}
