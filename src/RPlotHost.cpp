#include "stdafx.h"
#include "RPlotHost.h"

#define WM_ACTIVATE_PLOT (WM_USER + 100)

using namespace rplots;

HWND RPlotHost::m_hwndPlotWindow = NULL;
HHOOK RPlotHost::m_hOldHook = NULL;
bool RPlotHost::m_fProcessing = false;
std::unique_ptr<RPlotHost> RPlotHost::m_pInstance = NULL;

RPlotHost::RPlotHost(HWND wndPlotWindow) {
    m_hwndPlotWindow = wndPlotWindow;
    m_hOldHook = ::SetWindowsHookEx(WH_CBT, CBTProc, NULL, ::GetCurrentThreadId());
}

void RPlotHost::Init(HWND handle) {
    if (!m_pInstance && handle != NULL) {
        m_pInstance = std::unique_ptr<RPlotHost>(new RPlotHost(handle));
    }
}

void RPlotHost::Terminate() {
    if (m_hOldHook != NULL) {
        ::UnhookWindowsHookEx(m_hOldHook);
        m_hOldHook = NULL;

    }    
    m_pInstance = NULL;
}

LRESULT CALLBACK RPlotHost::CBTProc(
    _In_ int    nCode,
    _In_ WPARAM wParam,
    _In_ LPARAM lParam
    ) {
    if (nCode == HCBT_ACTIVATE) {
        if (!m_fProcessing) {
            m_fProcessing = true;
            HWND hwnd = (HWND)wParam;
            WCHAR buf[100];
            ::RealGetWindowClass(hwnd, buf, _countof(buf));
            if (wcscmp(buf, L"GraphApp") == 0) {
                if (m_hwndPlotWindow != GetParent(hwnd)) {
                    RECT rc;
                    ::SetWindowLong(hwnd, GWL_STYLE, WS_CHILD);
                    ::SetWindowLong(hwnd, GWL_EXSTYLE, 0);
                    ::SetMenu(hwnd, NULL);
                    ::SetWindowText(hwnd, NULL);

                    ::SetParent(hwnd, m_hwndPlotWindow);
                    ::GetClientRect(m_hwndPlotWindow, &rc);

                    if (rc.right < 0) {
                        rc.right = 200;
                        rc.bottom = 300;
                    }

                    ::SetWindowPos(hwnd, HWND_TOP, 0, 0, rc.right, rc.bottom, SWP_SHOWWINDOW | SWP_FRAMECHANGED);
                    ::PostMessage(m_hwndPlotWindow, WM_ACTIVATE_PLOT, 0, 0);
                }
            }
            m_fProcessing = false;
        }
    }

    return ::CallNextHookEx(m_hOldHook, nCode, wParam, lParam);
};
