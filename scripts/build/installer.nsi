!include "MUI2.nsh"

Name "Ultraudio"
OutFile "${OUTFILE}"

; Por defecto instalará en Program Files (o Program Files x86 si fuera 32 bits)
InstallDir "$PROGRAMFILES64\Ultraudio"

; Pedir privilegios de administrador para instalar en Archivos de Programa
RequestExecutionLevel admin

!define MUI_ABORTWARNING

; Páginas
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

; Idiomas
!insertmacro MUI_LANGUAGE "Spanish"
!insertmacro MUI_LANGUAGE "English"

Section "Instalación" SecInstall
    SetOutPath "$INSTDIR"
    
    ; Copiar todos los archivos desde la carpeta de publicación (se pasa por comando)
    File /r "${PUBLISH_DIR}"
    
    ; Crear desinstalador
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    
    ; Crear acceso directo en el Menú Inicio
    CreateDirectory "$SMPROGRAMS\Ultraudio"
    CreateShortcut "$SMPROGRAMS\Ultraudio\Ultraudio.lnk" "$INSTDIR\Ultraudio.exe"
    
    ; Crear acceso directo en el Escritorio
    CreateShortcut "$DESKTOP\Ultraudio.lnk" "$INSTDIR\Ultraudio.exe"
    
    ; Registrar para desinstalar en Panel de Control
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ultraudio" "DisplayName" "Ultraudio"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ultraudio" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ultraudio" "DisplayIcon" "$INSTDIR\Ultraudio.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ultraudio" "Publisher" "EsMeSolutions"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ultraudio" "DisplayVersion" "${VERSION}"
SectionEnd

Section "Uninstall"
    ; Eliminar archivos instalados (usamos /r para asegurar que borra todo el contenido)
    RMDir /r "$INSTDIR"
    
    ; Eliminar accesos directos
    Delete "$SMPROGRAMS\Ultraudio\Ultraudio.lnk"
    RMDir "$SMPROGRAMS\Ultraudio"
    Delete "$DESKTOP\Ultraudio.lnk"
    
    ; Eliminar registro
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ultraudio"
SectionEnd
