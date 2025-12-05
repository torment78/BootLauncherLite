// --- GLOBAL USING DIRECTIVES ---

// Core
global using System;
global using System.IO;
global using System.Linq;
global using System.Collections.Generic;
global using System.Collections.ObjectModel;
global using System.Threading.Tasks;
global using System.Diagnostics;
global using System.Net;
global using System.Net.Sockets;
global using System.Net.NetworkInformation;
global using System.Text.Json;
global using System.Runtime.InteropServices;
global using System.Text;
// WPF (Primary UI Framework)
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Data;
global using System.Windows.Input;
global using System.Windows.Media;
global using System.Windows.Media.Imaging;
global using System.Windows.Navigation;
global using BootLauncherLite.Tray;
global using BootLauncherLite.Views;
global using System.Windows.Interop;
// Our namespaces
global using BootLauncherLite.Models;
global using BootLauncherLite.Services;
global using System.ComponentModel;
global using BootLauncherLite.Audio;
global using System.Threading;
// IMPORTANT: WinForms in Tray ONLY
// We do NOT import WinForms globally — to avoid UI conflicts.
// TrayIconManager.cs will include its own private using.
