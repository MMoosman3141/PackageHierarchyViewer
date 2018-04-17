using NemMvvm;
using NuGet;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace PackageHierarchyViewer {
  public class MainViewModel : NotifyPropertyChanged {
    private readonly FrameworkName _frameworkName = new FrameworkName(".NETFramework, Version=4.6.1");

    private string _packageSource;
    private bool _allowPrerelease = false;
    private string _targetPackage = null;
    private List<string> _packages;
    private Command _getPackagesCmd;

    public string PackageSource {
      get => _packageSource;
      set => SetProperty(ref _packageSource, value, new[] { GetPackagesCmd });
    }
    public bool AllowPrerelease {
      get => _allowPrerelease;
      set => SetProperty(ref _allowPrerelease, value);
    }
    public string TargetPackage {
      get => _targetPackage;
      set => SetProperty(ref _targetPackage, value);
    }
    public List<string> Packages {
      get => _packages;
      set => SetProperty(ref _packages, value);
    }
    public Command GetPackagesCmd {
      get => _getPackagesCmd;
      set => SetProperty(ref _getPackagesCmd, value);
    }

    public MainViewModel() {
      Packages = new List<string>();
      GetPackagesCmd = new Command(GetPackages, CanGetPackages);
    }

    private void GetPackages() {
      Mouse.OverrideCursor = Cursors.Wait;

      Task.Run(() => {
        ConcurrentBag<IPackage> packages = new ConcurrentBag<IPackage>();

        IPackageRepository repository = PackageRepositoryFactory.Default.CreateRepository(PackageSource);

        Task packagesTask = Task.Run(() => {
          foreach(IPackage package in repository.GetPackages()) {
            if(!string.IsNullOrWhiteSpace(TargetPackage) && package.GetFullName() != TargetPackage) {
              continue;
            }

            if(VersionUtility.IsCompatible(_frameworkName, package.GetSupportedFrameworks())) {
              if(AllowPrerelease) {
                if(package.IsAbsoluteLatestVersion) {
                  packages.Add(package);
                } else if(package.IsLatestVersion) {
                  packages.Add(package);
                }
              }
            }
          }
        });

        Task listTask = Task.Run(() => {
          do {
            while(packages.TryTake(out IPackage package)) {
              GetValue(repository, _frameworkName, package, 0);
            }

            SpinWait.SpinUntil(() => packages.Count > 0 || packagesTask.IsCompleted);
          } while(packages.Count > 0 || !packagesTask.IsCompleted);
        });

        listTask.Wait();

      }).ContinueWith(task => {
        Dispatcher.CurrentDispatcher.Invoke(() => {
          Mouse.OverrideCursor = null;
        });
      });
    }

    private void GetValue(IPackageRepository repository, FrameworkName frameworkName, IPackage package, int indent) {
      Packages.Add($"{new string(' ', indent * 3)}{package}");

      foreach(PackageDependency dependency in package.GetCompatiblePackageDependencies(frameworkName)) {
        IPackage subPackage = repository.ResolveDependency(dependency, AllowPrerelease, true);
        GetValue(repository, frameworkName, subPackage, indent + 1);
      }
    }

    private bool CanGetPackages() {
      if(string.IsNullOrWhiteSpace(PackageSource)) {
        return false;
      }

      return true;
    }
  }
}
