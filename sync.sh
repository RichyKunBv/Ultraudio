#!/bin/bash

# es para hacer una mini sincronizacion, realmente ignorenlo ya que es algo mas para mi que para ustedes, nada mas para mas agilidad
# Tambien como yo trabajo en macOS los comandos son en UNIX y no funconarian bien en Linux y mucho menos en Windows

cd "$HOME/Ultraudio" || { echo "Error: No se encontró la carpeta"; exit 1; }

descargar() {
    echo "Obteniendo últimos cambios del repositorio..."
    git pull origin main
}

publicar() {
    git add .
    read -p "   >> Introduce el mensaje del commit: " mensaje

        git pull origin main --rebase
    
    # Validación para evitar commits vacíos
    if [ -z "$mensaje" ]; then
        echo "El mensaje no puede estar vacío. Cancelando publicación..."
        return
    fi
    
    git commit -m "$mensaje"
    git push origin main
}

# Pausa interactiva
press_any_key() {
    echo -e "\nPulsa cualquier tecla para volver al menú..."
    read -n 1 -s -r
}

# --- MENÚ PRINCIPAL ---
show_menu() {
    cd "$PROJECT_DIR" || exit 1
    clear
    echo -e "   1) Actualizar local (Pull)"
    echo -e "   2) Actualizar el repo (Push)"
    echo -e "   X) Salir"
    read -p "   >> Introduce tu elección: " choice
    echo ""

    case "$choice" in
        1) descargar; press_any_key ;;
        2) publicar; press_any_key ;;
        X|x) echo "Saliendo... ¡Hasta pronto!"; exit 0 ;;
        *) echo "Opción inválida. Por favor, intenta de nuevo."; sleep 2 ;;
    esac
}

# Bucle infinito para mantener el menú corriendo
while true; do
    show_menu
done