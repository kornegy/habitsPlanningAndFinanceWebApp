// Saves a server response as a file. Used for exports, which need the in-memory access token and therefore
// cannot be plain links. The object URL is revoked immediately after the click.
export function saveFile(fileName, contentType, bytes) {
  const blob = new Blob([bytes], { type: contentType });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.rel = 'noopener';
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
