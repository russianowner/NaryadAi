window.naryadAi = {
    setLanguage: function (lang) {
        document.documentElement.lang = lang;
    }
};
window.naryadAiDownload = function (fileName, contentType, base64Data) {
    try {
        const byteCharacters = atob(base64Data);
        const byteNumbers = new Uint8Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        const blob = new Blob([byteNumbers], {
            type: contentType
        });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => {
            URL.revokeObjectURL(url);
        }, 1000);
    }
    catch (error) {
        console.error("Ошибка скачивания файла:", error);
        throw error;
    }
};