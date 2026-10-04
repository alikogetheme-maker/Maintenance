using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("MaintenanceAddon")]
[assembly: AssemblyDescription("Add-on de gestion de la maintenance (type SAP PM) pour SAP Business One")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("MaintenanceAddon")]
[assembly: ComVisible(false)]
// Banc de test DI API (logique des services sans client SAP)
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MntTest")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MntUiTest")]
[assembly: Guid("5e8a1c2d-9b3f-4e7a-8c6d-1f2a3b4c5d6e")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
