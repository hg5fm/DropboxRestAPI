/*
 * The MIT License (MIT)
 * 
 * Copyright (c) 2014 Itay Sagui
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */


using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DropboxRestAPI.Models.Core;
using DropboxRestAPI.RequestsGenerators.Core;
using Newtonsoft.Json;

namespace DropboxRestAPI.Services.Core
{
    public class Metadata : IMetadata
    {

        private readonly RequestExecuter _requestExecuter;
        private readonly IMetadataRequestGenerator _requestGenerator;
        private readonly Options _options;

        public Metadata(RequestExecuter requestExecuter, IMetadataRequestGenerator requestGenerator, Options options)
        {
            _requestExecuter = requestExecuter;
            _requestGenerator = requestGenerator;
            _options = options;
        }

        #region Implementation of IMetadata

        public async Task<MetaData> FilesAsync(string path, Stream targetStream, string rev = null, string asTeamMember = null)
        {
            MetaData fileMetadata = null;
            HttpResponseMessage restResponse = await _requestExecuter.Execute(() => _requestGenerator.Files(_options.Root, path, rev, asTeamMember)).ConfigureAwait(false);
            await _requestExecuter.CheckForError(restResponse, false).ConfigureAwait(false);

            long? length = restResponse.Content.Headers.ContentLength;
            if (length == null)
            {
                IEnumerable<string> metadatas;
                if (restResponse.Headers.TryGetValues("x-dropbox-metadata", out metadatas))
                {
                    string metadata = metadatas.FirstOrDefault();
                    if (metadata != null)
                    {
                        fileMetadata = JsonConvert.DeserializeObject<MetaData>(metadata);
                        length = fileMetadata.bytes;
                    }
                }
            }
            string etag = "";
            IEnumerable<string> etags;
            if (restResponse.Headers.TryGetValues("etag", out etags))
                etag = etags.FirstOrDefault();


            long? read = 0;
            do
            {
                long? from = read;
                long? to = read + _options.ChunkSize;
                if (to > length)
                    to = length;

                restResponse =
                    await
                        _requestExecuter.Execute(() => _requestGenerator.FilesRange(_options.Root, path, from.Value, to.Value - 1, etag, rev, asTeamMember)).ConfigureAwait(false);
                await restResponse.Content.CopyToAsync(targetStream).ConfigureAwait(false);

                read += restResponse.Content.Headers.ContentLength;

                if (read >= length)
                    break;
            } while (restResponse.StatusCode == HttpStatusCode.PartialContent);

            return fileMetadata;
        }

        public async Task<MetaData> FilesPutAsync(Stream content, string path, string locale = null, bool overwrite = true, string parent_rev = null, bool autorename = true, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<MetaData>(() => _requestGenerator.FilesPut(_options.Root, content, path, locale, overwrite, parent_rev, autorename, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<MetaData> MetadataAsync(string path, int file_limit = 10000, string hash = null, bool list = true, bool include_deleted = false, string rev = null, string locale = null, bool include_media_info = false, bool include_membership = false, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<MetaData>(() => _requestGenerator.Metadata(_options.Root, path, file_limit, hash, list, include_deleted, rev, locale, include_media_info, include_membership, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<Entries> DeltaAsync(string cursor = null, string locale = null, string path_prefix = null, bool include_media_info = false, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<Entries>(() => _requestGenerator.Delta(cursor, locale, path_prefix, include_media_info, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<Entries> DeltaLatestCursorAsync(string path_prefix = null, bool include_media_info = false, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<Entries>(() => _requestGenerator.DeltaLatestCursor(path_prefix, include_media_info, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<LongPollDelta> LongPollDeltaAsync(string cursor = null, int timeout = 30, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<LongPollDelta>(() => _requestGenerator.LongPollDelta(cursor, timeout, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<IEnumerable<Metadata>> RevisionsAsync(string path, int rev_limit = 10, string locale = null, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<IEnumerable<Metadata>>(() => _requestGenerator.Revisions(_options.Root, path, rev_limit, locale, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<Metadata> RestoreAsync(string path, string rev = null, string locale = null, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<Metadata>(() => _requestGenerator.Restore(_options.Root, path, rev, locale, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<IEnumerable<MetaData>> SearchAsync(string path, string query, int file_limit = 1000, bool include_deleted = false, string locale = null, bool include_membership = false, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<IEnumerable<MetaData>>(() => _requestGenerator.Search(_options.Root, path, query, file_limit, include_deleted, locale, include_membership, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<SharedLink> SharesAsync(string path, string locale = null, bool short_url = true, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<SharedLink>(() => _requestGenerator.Shares(_options.Root, path, locale, short_url, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<MediaLink> MediaAsync(string path, string locale = null, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<MediaLink>(() => _requestGenerator.Media(_options.Root, path, locale, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<CopyRef> CopyRefAsync(string path, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<CopyRef>(() => _requestGenerator.Media(_options.Root, path, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<Stream> ThumbnailsAsync(string path, string format = "jpeg", string size = "s", string asTeamMember = null)
        {
            var restResponse = await _requestExecuter.Execute(() => _requestGenerator.Thumbnails(_options.Root, path, format, size, asTeamMember)).ConfigureAwait(false);
            await _requestExecuter.CheckForError(restResponse, false).ConfigureAwait(false);

            return await restResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
        }

        public async Task<Preview> PreviewsAsync(string path, string rev = null, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<Preview>(() => _requestGenerator.Previews(_options.Root, path, rev, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<ChunkedUpload> ChunkedUploadAsync(byte[] content, int contentOffset, int count, string uploadId = null, long? offset = null, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<ChunkedUpload>(() => _requestGenerator.ChunkedUpload(content, contentOffset, count, uploadId, offset, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<Metadata> CommitChunkedUploadAsync(string path, string uploadId, string locale = null, bool overwrite = true, string parent_rev = null, bool autorename = true, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<Metadata>(() => _requestGenerator.CommitChunkedUpload(_options.Root, path, uploadId, locale, overwrite, parent_rev, autorename, asTeamMember)).ConfigureAwait(false);
        }

        public async Task<IEnumerable<Metadata>> SharedFoldersAsync(string id = null, string asTeamMember = null)
        {
            return await _requestExecuter.Execute<IEnumerable<Metadata>>(() => _requestGenerator.SharedFolders(id, asTeamMember)).ConfigureAwait(false);
        }

        #endregion
    }
}