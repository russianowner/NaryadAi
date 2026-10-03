window.naryadAiDownload = (fileName, contentType, base64Content) => {
    const raw = atob(base64Content);
    const bytes = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);

    const blob = new Blob([bytes], { type: contentType });
    const link = document.createElement("a");
    const url = URL.createObjectURL(blob);
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
};
