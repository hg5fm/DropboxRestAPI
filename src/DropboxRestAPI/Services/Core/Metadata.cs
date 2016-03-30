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


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DropboxRestAPI.Models.Core;
using DropboxRestAPI.RequestsGenerators.Core;
using DropboxRestAPI.Utils;
using Newtonsoft.Json;

namespace DropboxRestAPI.Services.Core
{
    public class Metadata : IMetadata
    {

        private readonly IRequestExecuter _requestExecuter;
        private readonly IMetadataRequestGenerator _requestGenerator;
        private readonly Options _options;

        public Metadata(IRequestExecuter requestExecuter, IMetadataRequestGenerator requestGenerator, Options options)
        {
            _requestExecuter = requestExecuter;
            _requestGenerator = requestGenerator;
            _options = options;
        }

        #region Implementation of IMetadata

        public async Task<MetaData> FilesAsync(string path, Stream targetStream, string rev = null,
            string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            MetaData fileMetadata = null;
            string etag = "";
            long? length = null;
            try
            {
                using (var restResponse = await _requestExecuter.Execute(
                                () => _requestGenerator.Files(_options.Root, path, rev, asTeamMember),
                                cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    await _requestExecuter.CheckForError(restResponse, false, cancellationToken).ConfigureAwait(false);
                    length = restResponse.Content.Headers.ContentLength;
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
                    IEnumerable<string> etags;
                    if (restResponse.Headers.TryGetValues("etag", out etags))
                        etag = etags.FirstOrDefault();
                }
            }
            catch (HttpException)
            {
                // Retry the Files request now with GET method in order to retrieve the full error message from the request                
                using (var restResponseWithContent = await _requestExecuter.Execute(
                    () => _requestGenerator.Files(_options.Root, path, rev, asTeamMember, true),
                    cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    await
                        _requestExecuter.CheckForError(restResponseWithContent, false, cancellationToken)
                            .ConfigureAwait(false);
                }
                throw;
            }


            long read = 0;
            bool hasMore = true;
            do
            {
                long from = read;
                long to = read + _options.ChunkSize;

                using (var restResponse2 = await _requestExecuter.Execute(() => _requestGenerator.FilesRange(_options.Root, path, from, to - 1, etag, rev, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        using (cancellationToken.Register(restResponse2.Dispose))
                            await restResponse2.Content.CopyToAsync(targetStream).ConfigureAwait(false);
                    }
                    catch (HttpRequestException ex)
                    {
                        if (ex.InnerException != null && ex.InnerException is ObjectDisposedException)
                            cancellationToken.ThrowIfCancellationRequested();
                        throw;
                    }
                    catch (ObjectDisposedException)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    read += (restResponse2.Content.Headers.ContentLength ?? 0);
                    if (length.HasValue && read >= length.GetValueOrDefault())
                        hasMore = false;
                    else if (restResponse2.StatusCode == HttpStatusCode.OK)
                        hasMore = false;
                }
            } while (hasMore);


            return fileMetadata;
        }

        public async Task<MetaData> FilesPutAsync(Stream content, string path, string locale = null, bool overwrite = true, string parent_rev = null, bool autorename = true, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<MetaData>(() => _requestGenerator.FilesPut(_options.Root, content, path, locale, overwrite, parent_rev, autorename, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<MetaData> MetadataAsync(string path, int file_limit = 10000, string hash = null, bool list = true, bool include_deleted = false, string rev = null, string locale = null, bool include_media_info = false, bool include_membership = false, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<MetaData>(() => _requestGenerator.Metadata(_options.Root, path, file_limit, hash, list, include_deleted, rev, locale, include_media_info, include_membership, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<Entries> DeltaAsync(string cursor = null, string locale = null, string path_prefix = null, bool include_media_info = false, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<Entries>(() => _requestGenerator.Delta(cursor, locale, path_prefix, include_media_info, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<DeltaCursor> DeltaLatestCursorAsync(string path_prefix = null, bool include_media_info = false, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<DeltaCursor>(() => _requestGenerator.DeltaLatestCursor(path_prefix, include_media_info, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<LongPollDelta> LongPollDeltaAsync(string cursor = null, int timeout = 30, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<LongPollDelta>(() => _requestGenerator.LongPollDelta(cursor, timeout, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<IEnumerable<MetaData>> RevisionsAsync(string path, int rev_limit = 10, string locale = null, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<IEnumerable<MetaData>>(() => _requestGenerator.Revisions(_options.Root, path, rev_limit, locale, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<MetaData> RestoreAsync(string path, string rev = null, string locale = null, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<MetaData>(() => _requestGenerator.Restore(_options.Root, path, rev, locale, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<IEnumerable<MetaData>> SearchAsync(string path, string query, int file_limit = 1000, bool include_deleted = false, string locale = null, bool include_membership = false, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<IEnumerable<MetaData>>(() => _requestGenerator.Search(_options.Root, path, query, file_limit, include_deleted, locale, include_membership, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<SharedLink> SharesAsync(string path, string locale = null, bool short_url = true, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<SharedLink>(() => _requestGenerator.Shares(_options.Root, path, locale, short_url, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<MediaLink> MediaAsync(string path, string locale = null, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<MediaLink>(() => _requestGenerator.Media(_options.Root, path, locale, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<CopyRef> CopyRefAsync(string path, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<CopyRef>(() => _requestGenerator.Media(_options.Root, path, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<Stream> ThumbnailsAsync(string path, string format = "jpeg", string size = "s", string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var restResponse = await _requestExecuter.Execute(() => _requestGenerator.Thumbnails(_options.Root, path, format, size, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);

            return await restResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
        }

        public async Task<Preview> PreviewsAsync(string path, string rev = null, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var restResponse = await _requestExecuter.Execute(() => _requestGenerator.Previews(Options.AutoRoot, path, rev, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);

            var preview = new Preview
            {
                Content = await restResponse.Content.ReadAsStreamAsync().ConfigureAwait(false),
                ContentType = restResponse.Content.Headers.ContentType.MediaType
            };
            return preview;
        }

        public async Task<ChunkedUpload> ChunkedUploadAsync(byte[] content, int count, string uploadId = null, long? offset = null, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<ChunkedUpload>(() => _requestGenerator.ChunkedUpload(content, count, uploadId, offset, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<MetaData> CommitChunkedUploadAsync(string path, string uploadId, string locale = null, bool overwrite = true, string parent_rev = null, bool autorename = true, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<MetaData>(() => _requestGenerator.CommitChunkedUpload(_options.Root, path, uploadId, locale, overwrite, parent_rev, autorename, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<SharedFolder> SharedFolderAsync(string shared_folder_id = null, bool? include_membership = true, bool show_unmounted = false, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<SharedFolder>(() => _requestGenerator.SharedFolders(shared_folder_id, include_membership, show_unmounted, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }


        public async Task<IEnumerable<SharedFolder>> SharedFoldersAsync(bool? include_membership = true, bool show_unmounted = false, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<IEnumerable<SharedFolder>>(() => _requestGenerator.SharedFolders(null, include_membership, show_unmounted, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }


        public async Task<SaveUrl> SaveUrlAsync(string path, string url, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<SaveUrl>(() => _requestGenerator.SaveUrl(_options.Root, path, url, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<SaveUrlJob> SaveUrlJobAsync(string jobId, string asTeamMember = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _requestExecuter.Execute<SaveUrlJob>(() => _requestGenerator.SaveUrlJob(jobId, asTeamMember), cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        #endregion
    }
}