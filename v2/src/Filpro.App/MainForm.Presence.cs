using System.Runtime.InteropServices;
using Filpro.Core;

namespace Filpro.App;

/// <summary>
/// Verrou de présence quand l'affichage tourne sous un autre compte que FILPRO élevé, ou sous un compte indéterminé :
/// session verrouillée, déconnectée ou fermée, inactivité, âge du processus et refus répétés. Verrouillé, FILPRO ne lance
/// plus rien ; les opérations en cours continuent et la fermeture reste possible.
/// </summary>
internal sealed partial class MainForm
{
    private const string LockedTitleSuffix = " — verrouillé";

    // Sélecteur de fichiers affiché : seul lui peut être annulé par FILPRO, jamais une autre boîte de dialogue.
    private bool pickerOpen;

    private string CurrentTitle => composition.WindowTitle + (presence.Latched ? LockedTitleSuffix : "");

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Notifications de la seule session de FILPRO ; un échec laisse les autres motifs de verrouillage actifs.
        if (!PresenceNative.WTSRegisterSessionNotification(Handle, PresenceNative.NotifyForThisSession))
            TechnicalLog.Instance.Warning("Notifications de session Windows indisponibles (" + Marshal.GetLastPInvokeError() + ").");
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _ = PresenceNative.WTSUnRegisterSessionNotification(Handle);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        // Reçu dès la création de la fenêtre, donc parfois avant que la situation soit connue : la présence le garde alors.
        if (m.Msg == PresenceNative.SessionChangeMessage && NativeWindowPolicy.PresenceReasonFor((int)m.WParam) is { } reason)
            presence.SessionChanged(reason);
        base.WndProc(ref m);
    }

    /// <summary>
    /// Relevé toutes les 10 s, sur le fil de l'interface : état courant de la session (une notification manquée compte
    /// quand même), puis dernière saisie lue avant le compteur courant.
    /// </summary>
    private void PollPresence()
    {
        if (!presence.Armed || presence.Latched) return;
        CheckSessionState();
        var input = new PresenceNative.LastInputInfo { Size = (uint)Marshal.SizeOf<PresenceNative.LastInputInfo>() };
        // Sans dernière saisie lisible, l'inactivité n'est pas établie : seuls l'âge et les écarts de relevé comptent.
        var lastInput = PresenceNative.GetLastInputInfo(ref input) ? input.Time : unchecked((uint)Environment.TickCount64);
        presence.Poll(lastInput, unchecked((uint)Environment.TickCount64));
    }

    /// <summary>Session de FILPRO verrouillée ou déconnectée à cet instant (WTSSessionInfoEx) ; une lecture impossible ne conclut rien.</summary>
    private void CheckSessionState()
    {
        if (!PresenceNative.WTSQuerySessionInformationW(IntPtr.Zero, PresenceNative.CurrentSession, PresenceNative.SessionInfoEx, out var buffer, out var bytes))
            return;
        try
        {
            // WTSINFOEXW : Level (4 octets), puis WTSINFOEX_LEVEL1_W aligné sur 8 : SessionId, SessionState, SessionFlags.
            if (bytes < 20 || Marshal.ReadInt32(buffer, 0) != 1) return;
            if (NativeWindowPolicy.SessionStateReason(Marshal.ReadInt32(buffer, 12), Marshal.ReadInt32(buffer, 16)) is { } reason)
                presence.SessionChanged(reason);
        }
        finally { PresenceNative.WTSFreeMemory(buffer); }
    }

    /// <summary>Le verrouillage peut venir du fil du pont (refus répétés, lancement) : ses effets passent sur le fil de l'interface.</summary>
    private void OnPresenceChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(ApplyPresenceLock); }
        catch (InvalidOperationException) { /* Fenêtre détruite : la composition suivante lira l'état verrouillé. */ }
    }

    private void ApplyPresenceLock()
    {
        if (IsDisposed) return;
        TechnicalLog.Instance.Warning("FILPRO verrouillé dans cette session (" + NativeWindowPolicy.PresenceReasonText(presence.Reason)
            + ") : les opérations en cours continuent ; relancer FILPRO pour en lancer d’autres.");
        if (Volatile.Read(ref shuttingDown) == 0) Text = CurrentTitle;
        RefusePendingConfirmation();
        if (CancelOpenPicker()) TechnicalLog.Instance.Info("Sélecteur de fichiers fermé : FILPRO est verrouillé.");
        PostEvent("presence.changed", new { Locked = true });
    }

    /// <summary>
    /// Ferme le sélecteur de fichiers ouvert par cette fenêtre, comme Annuler (WM_COMMAND IDCANCEL) : boîte de dialogue
    /// Windows (#32770) de ce fil, possédée par cette fenêtre. Vrai si une boîte a été trouvée.
    /// </summary>
    private bool CancelOpenPicker()
    {
        if (!pickerOpen || IsDisposed || !IsHandleCreated) return false;
        var owner = Handle;
        var found = new List<IntPtr>();
        PresenceNative.EnumThreadWndProc callback = (window, _) =>
        {
            if (PresenceNative.GetWindow(window, PresenceNative.Owner) == owner && IsDialogClass(window)) found.Add(window);
            return true;
        };
        _ = PresenceNative.EnumThreadWindows(PresenceNative.GetCurrentThreadId(), callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        foreach (var window in found)
            _ = PresenceNative.PostMessage(window, PresenceNative.CommandMessage, PresenceNative.CancelCommand, IntPtr.Zero);
        return found.Count > 0;
    }

    private static bool IsDialogClass(IntPtr window)
    {
        var buffer = new char[16];
        var length = PresenceNative.GetClassName(window, buffer, buffer.Length);
        return length == 6 && new string(buffer, 0, length) == "#32770";
    }

    private static class PresenceNative
    {
        // WM_WTSSESSION_CHANGE, NOTIFY_FOR_THIS_SESSION, WTS_CURRENT_SESSION, WTSSessionInfoEx.
        internal const int SessionChangeMessage = 0x02B1;
        internal const int NotifyForThisSession = 0;
        internal const int CurrentSession = -1;
        internal const int SessionInfoEx = 25;
        // GW_OWNER, WM_COMMAND, IDCANCEL.
        internal const uint Owner = 4;
        internal const uint CommandMessage = 0x0111;
        internal static readonly IntPtr CancelCommand = 2;

        // LASTINPUTINFO.
        [StructLayout(LayoutKind.Sequential)]
        internal struct LastInputInfo
        {
            public uint Size;
            public uint Time;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal delegate bool EnumThreadWndProc(IntPtr window, IntPtr parameter);

        [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSRegisterSessionNotification(IntPtr window, int flags);

        [DllImport("wtsapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSUnRegisterSessionNotification(IntPtr window);

        [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

        [DllImport("wtsapi32.dll")]
        internal static extern void WTSFreeMemory(IntPtr memory);

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetLastInputInfo(ref LastInputInfo info);

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumThreadWindows(uint threadId, EnumThreadWndProc callback, IntPtr parameter);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
        internal static extern int GetClassName(IntPtr window, [Out] char[] className, int maxCount);

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
